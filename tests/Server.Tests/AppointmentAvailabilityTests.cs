using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Scheduling;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class AppointmentAvailabilityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static string Path(Guid service, Guid member, string day = "2026-10-16") =>
        $"/api/appointments/availability?serviceId={service}&staffMemberId={member}&date={day}";
    private static async Task<AppointmentEndpoints.AvailabilityResponse> Read(HttpClient owner, Guid service, Guid member)
    {
        using var response = await owner.GetAsync(Path(service, member), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadAsStringAsync(Token);
        foreach (var field in new[] { "customerName", "customerPhone", "requestKey", "actorId", "appointmentId" }) Assert.DoesNotContain(field, json);
        return Assert.IsType<AppointmentEndpoints.AvailabilityResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AvailabilityResponse>(Token));
    }
    private static void Reasons(AppointmentEndpoints.AvailabilityResponse result, params string[] codes)
    {
        Assert.Empty(result.StartsAt);
        Assert.Equal(codes.Order(), result.UnavailableReasons.Select(item => item.Code).Order());
        Assert.All(result.UnavailableReasons, item => Assert.InRange(item.Message.Length, 1, 500));
    }

    [Fact]
    public async Task ExplainsIndependentBlockersAndUsesEffectiveDateExceptions()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        var assignment = await db.StaffServiceAssignments.SingleAsync(item => item.StaffMemberId == member.Id, Token);
        db.StaffServiceAssignments.Remove(assignment);
        var businessDay = await db.BusinessOpeningDays.SingleAsync(item => item.Day == 4, Token);
        var staffDay = await db.StaffWorkingDays.SingleAsync(item => item.StaffMemberId == member.Id && item.Day == 4, Token);
        businessDay.IsClosed = staffDay.IsClosed = true;
        businessDay.OpensAtMinute = businessDay.ClosesAtMinute = staffDay.OpensAtMinute = staffDay.ClosesAtMinute = null;
        await db.SaveChangesAsync(Token);
        Reasons(await Read(owner, service.Id, member.Id), "ServiceNotAssigned", "BusinessClosed", "StaffClosed");

        db.StaffServiceAssignments.Add(new() { StaffMemberId = member.Id, ServiceDefinitionId = service.Id });
        var day = new DateOnly(2026, 10, 16);
        db.BusinessDateExceptions.Add(new() { ScheduleId = 1, Date = day, OpensAtMinute = 540, ClosesAtMinute = 1140 });
        db.StaffDateExceptions.Add(new() { StaffMemberId = member.Id, Date = day, OpensAtMinute = 540, ClosesAtMinute = 1140 });
        await db.SaveChangesAsync(Token);
        var open = await Read(owner, service.Id, member.Id); Assert.NotEmpty(open.StartsAt); Assert.Empty(open.UnavailableReasons);

        (await db.BusinessHoursSchedules.SingleAsync(Token)).IsConfigured = false; service.IsActive = member.IsActive = false;
        await db.SaveChangesAsync(Token);
        Reasons(await Read(owner, service.Id, member.Id), "BusinessHoursMissing", "ServiceInactive", "StaffInactive");
        (await db.BusinessHoursSchedules.SingleAsync(Token)).IsConfigured = true; service.IsActive = member.IsActive = true;
        db.StaffWorkingDays.RemoveRange(await db.StaffWorkingDays.Where(item => item.StaffMemberId == member.Id).ToArrayAsync(Token));
        await db.SaveChangesAsync(Token);
        Reasons(await Read(owner, service.Id, member.Id), "StaffHoursMissing");
        Assert.Empty(await db.Appointments.ToArrayAsync(Token));
        using var anonymous = app.CreateClient(); using var denied = await anonymous.GetAsync(Path(service.Id, member.Id), Token);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task DistinguishesDurationBuffersBreaksLeaveOccupiedAndBookingWindowWithoutChangingSlots()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var clock = new Clock(); var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        var policy = await db.BookingPolicies.SingleAsync(Token); policy.LeadMinutes = 0; policy.SlotMinutes = 15; policy.HorizonDays = 30;
        (await db.BusinessOpeningDays.SingleAsync(item => item.Day == 4, Token)).ClosesAtMinute = 600;
        (await db.StaffWorkingDays.SingleAsync(item => item.StaffMemberId == member.Id && item.Day == 4, Token)).ClosesAtMinute = 600;
        service.DurationMinutes = 120; await db.SaveChangesAsync(Token);
        var tooLong = await Read(owner, service.Id, member.Id); Reasons(tooLong, "WorkingTime"); Assert.Contains("120 dk", tooLong.UnavailableReasons[0].Message);
        service.DurationMinutes = 40; service.BufferBeforeMinutes = 10; service.BufferAfterMinutes = 15; await db.SaveChangesAsync(Token);
        var buffered = await Read(owner, service.Id, member.Id); Reasons(buffered, "WorkingTime"); Assert.Contains("25 dk tampon", buffered.UnavailableReasons[0].Message);
        service.DurationMinutes = 20; service.BufferBeforeMinutes = service.BufferAfterMinutes = 5; await db.SaveChangesAsync(Token);
        var baseline = await Read(owner, service.Id, member.Id);
        Assert.Equal(new[] { new DateTime(2026, 10, 16, 6, 15, 0, DateTimeKind.Utc), new DateTime(2026, 10, 16, 6, 30, 0, DateTimeKind.Utc) }, baseline.StartsAt);
        Assert.Empty(baseline.UnavailableReasons);

        var pause = new BusinessWeeklyBreak { ScheduleId = 1, Day = 4, Position = 1, StartsAtMinute = 560, EndsAtMinute = 580 };
        db.BusinessWeeklyBreaks.Add(pause); await db.SaveChangesAsync(Token);
        Reasons(await Read(owner, service.Id, member.Id), "Breaks");
        db.BusinessWeeklyBreaks.Remove(pause);
        var leave = new StaffLeave { Id = Guid.NewGuid(), StaffMemberId = member.Id, StartsAt = new(2026, 10, 16, 6, 0, 0, DateTimeKind.Utc), EndsAt = new(2026, 10, 16, 7, 0, 0, DateTimeKind.Utc), RequestHash = new string('A', 64) };
        db.StaffLeaves.Add(leave); await db.SaveChangesAsync(Token);
        Reasons(await Read(owner, service.Id, member.Id), "Leave");
        db.StaffLeaves.Remove(leave); service.DurationMinutes = 60; service.BufferBeforeMinutes = service.BufferAfterMinutes = 0; await db.SaveChangesAsync(Token);
        using var create = await PostAsync(owner, "/api/appointments/", new AppointmentEndpoints.CreateRequest(Guid.NewGuid(), service.Id, service.Version, member.Id,
            "2026-10-16T09:00:00+03:00", "Sentetik müşteri", "05000000001"), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var booked = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await create.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        service.DurationMinutes = 20; service.BufferBeforeMinutes = service.BufferAfterMinutes = 5; await db.SaveChangesAsync(Token);
        Reasons(await Read(owner, service.Id, member.Id), "Occupied");
        using var cancel = await PostAsync(owner, $"/api/appointments/{booked.Id}/cancel", new { booked.Version, RequestKey = Guid.NewGuid() }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(baseline.StartsAt, (await Read(owner, service.Id, member.Id)).StartsAt);

        clock.Now = new(2026, 10, 16, 6, 0, 0, TimeSpan.Zero); policy.LeadMinutes = 120; await db.SaveChangesAsync(Token);
        using var currentOwner = await InviteOwnerAsync(seed);
        Reasons(await Read(currentOwner, service.Id, member.Id), "BookingWindow");
        using var outside = await currentOwner.GetAsync(Path(service.Id, member.Id, "2026-10-15"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode); Assert.Contains("\"date\"", await outside.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task MoveReasonsUseSnapshotDurationAndExcludeTheOriginalAppointment()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        using var create = await PostAsync(owner, "/api/appointments/", new AppointmentEndpoints.CreateRequest(Guid.NewGuid(), service.Id, service.Version, member.Id,
            "2026-10-16T10:00:00+03:00", "Sentetik müşteri", "05000000001"), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var booked = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await create.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        service.DurationMinutes = 700; await db.SaveChangesAsync(Token);
        Reasons(await Read(owner, service.Id, member.Id), "WorkingTime");
        var path = $"/api/appointments/{booked.Id}/move-availability?staffMemberId={member.Id}&date=2026-10-16";
        using var preview = await owner.GetAsync(path, Token); Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var movable = Assert.IsType<AppointmentMoveEndpoints.AvailabilityResponse>(await preview.Content.ReadFromJsonAsync<AppointmentMoveEndpoints.AvailabilityResponse>(Token));
        Assert.Contains(booked.StartsAt, movable.StartsAt); Assert.Empty(movable.UnavailableReasons); Assert.Equal(booked.Version, movable.Version);
        db.StaffServiceAssignments.Remove(await db.StaffServiceAssignments.SingleAsync(item => item.StaffMemberId == member.Id, Token)); await db.SaveChangesAsync(Token);
        using var denied = await owner.GetAsync(path, Token); Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        var blocked = Assert.IsType<AppointmentMoveEndpoints.AvailabilityResponse>(await denied.Content.ReadFromJsonAsync<AppointmentMoveEndpoints.AvailabilityResponse>(Token));
        Assert.Empty(blocked.StartsAt); Assert.Equal("ServiceNotAssigned", Assert.Single(blocked.UnavailableReasons).Code);
        var original = await db.AppointmentTimeBlocks.AsNoTracking().SingleAsync(Token);
        Assert.Equal(booked.StartsAt, original.StartsAt); Assert.Equal(booked.DurationMinutes, original.DurationMinutes);
        Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
    }
}
