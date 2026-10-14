using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Business;
using Server.Features.Scheduling;
using Server.Features.Services;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class AppointmentBufferTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 14, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static AppointmentEndpoints.CreateRequest Request(ServiceDefinition service, Guid member, int hour = 10, int minute = 0) =>
        new(Guid.NewGuid(), service.Id, service.Version, member,
            new DateTimeOffset(2026, 10, 15, hour, minute, 0, TimeSpan.FromHours(3)).ToString("O", CultureInfo.InvariantCulture),
            "Sentetik müşteri", "05000000001");
    private static async Task<T> Body<T>(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(Token));
    }

    [Fact]
    public async Task BufferApiValidatesAuthorizationFieldsVersionAndNoOp()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); var path = $"/api/services/{service.Id}/buffers";
        using var anonymous = app.CreateClient(); using var denied = await anonymous.GetAsync(path, Token); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        using var staffRead = await staff.GetAsync(path, Token); Assert.Equal(HttpStatusCode.Forbidden, staffRead.StatusCode);
        using var staffWrite = await PostAsync(staff, path, new { version = service.Version, bufferBeforeMinutes = 5, bufferAfterMinutes = 10 }, await GetCsrfAsync(staff)); Assert.Equal(HttpStatusCode.Forbidden, staffWrite.StatusCode);
        using var csrfMissing = await PostAsync(owner, path, new { version = service.Version, bufferBeforeMinutes = 5, bufferAfterMinutes = 10 }, null); Assert.Equal(HttpStatusCode.BadRequest, csrfMissing.StatusCode);
        var csrf = await GetCsrfAsync(owner);
        foreach (var invalid in new[] { new ServiceDefinitionEndpoints.BuffersRequest(service.Version, null, 0), new(service.Version, 0, null), new(service.Version, -1, 0), new(service.Version, 0, 121), new(Guid.Empty, 0, 0) })
        {
            using var rejected = await PostAsync(owner, path, invalid, csrf); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using var fractional = await PostAsync(owner, path, new { version = service.Version, bufferBeforeMinutes = 0.5, bufferAfterMinutes = 0 }, csrf); Assert.Equal(HttpStatusCode.BadRequest, fractional.StatusCode);
        using var noOp = await PostAsync(owner, path, new { version = service.Version, bufferBeforeMinutes = 0, bufferAfterMinutes = 0 }, csrf);
        Assert.Equal(service.Version, (await Body<ServiceDefinitionEndpoints.BuffersResponse>(noOp, HttpStatusCode.OK)).Version);
        Assert.Empty(await db.ServiceDefinitionAudits.ToArrayAsync(Token));
        using var write = await PostAsync(owner, path, new { version = service.Version, bufferBeforeMinutes = 120, bufferAfterMinutes = 120 }, csrf);
        var changed = await Body<ServiceDefinitionEndpoints.BuffersResponse>(write, HttpStatusCode.OK); Assert.NotEqual(service.Version, changed.Version);
        using var stale = await PostAsync(owner, path, new { version = service.Version, bufferBeforeMinutes = 0, bufferAfterMinutes = 0 }, csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var read = await owner.GetAsync(path, Token); Assert.Equal(changed, await Body<ServiceDefinitionEndpoints.BuffersResponse>(read, HttpStatusCode.OK));
        Assert.True(read.Headers.CacheControl?.NoStore); Assert.Equal("Updated", (await db.ServiceDefinitionAudits.SingleAsync(Token)).Kind);
        await db.Entry(service).ReloadAsync(Token); service.IsDeleted = true; service.IsActive = false; await db.SaveChangesAsync(Token);
        using var deleted = await owner.GetAsync(path, Token); Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        using var deletedWrite = await PostAsync(owner, path, new { version = changed.Version, bufferBeforeMinutes = 0, bufferAfterMinutes = 0 }, csrf); Assert.Equal(HttpStatusCode.NotFound, deletedWrite.StatusCode);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task UpdatesPreservePriorSnapshotsAndExistingServiceMutationsPreserveBuffers()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.AsNoTracking().SingleAsync(Token); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var request = Request(service, member.Id);
        using var first = await PostAsync(owner, "/api/appointments/", request, csrf); var old = await Body<AppointmentEndpoints.AppointmentResponse>(first, HttpStatusCode.Created);
        using var write = await PostAsync(owner, $"/api/services/{service.Id}/buffers", new { version = service.Version, bufferBeforeMinutes = 7, bufferAfterMinutes = 11 }, csrf);
        var buffers = await Body<ServiceDefinitionEndpoints.BuffersResponse>(write, HttpStatusCode.OK);
        using var update = await PostAsync(owner, $"/api/services/{service.Id}", new { version = buffers.Version, name = "Yeni sentetik hizmet", durationMinutes = 25, price = "456.78" }, csrf);
        var updated = await Body<ServiceDefinitionEndpoints.ServiceResponse>(update, HttpStatusCode.OK);
        using var deactivate = await PostAsync(owner, $"/api/services/{service.Id}/status", new { version = updated.Version, isActive = false }, csrf);
        var passive = await Body<ServiceDefinitionEndpoints.ServiceResponse>(deactivate, HttpStatusCode.OK);
        using var activate = await PostAsync(owner, $"/api/services/{service.Id}/status", new { version = passive.Version, isActive = true }, csrf);
        var active = await Body<ServiceDefinitionEndpoints.ServiceResponse>(activate, HttpStatusCode.OK);
        using var replay = await PostAsync(owner, "/api/appointments/", request, csrf); Assert.Equal(old, await Body<AppointmentEndpoints.AppointmentResponse>(replay, HttpStatusCode.OK));
        using var stale = await PostAsync(owner, "/api/appointments/", Request(service, member.Id, 13), csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var next = await PostAsync(owner, "/api/appointments/", Request(service, member.Id, 13) with { ServiceVersion = active.Version }, csrf);
        var saved = await Body<AppointmentEndpoints.AppointmentResponse>(next, HttpStatusCode.Created);
        Assert.Equal(7, saved.BufferBeforeMinutes); Assert.Equal(11, saved.BufferAfterMinutes); Assert.Equal(25, saved.DurationMinutes);
        Assert.Equal(saved.StartsAt.AddMinutes(-7), saved.OccupiedStartsAt); Assert.Equal(saved.EndsAt.AddMinutes(11), saved.OccupiedEndsAt);
        Assert.Equal(old.StartsAt, old.OccupiedStartsAt); Assert.Equal(old.EndsAt, old.OccupiedEndsAt); Assert.Equal(0, old.BufferBeforeMinutes);
        using var remove = await PostAsync(owner, $"/api/services/{service.Id}/delete", new { version = active.Version }, csrf); Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var retained = await db.ServiceDefinitions.AsNoTracking().SingleAsync(Token); Assert.Equal(7, retained.BufferBeforeMinutes); Assert.Equal(11, retained.BufferAfterMinutes);
        using var read = await owner.GetAsync($"/api/appointments/{saved.Id}", Token); Assert.Equal(saved, await Body<AppointmentEndpoints.AppointmentResponse>(read, HttpStatusCode.OK));
    }

    [Fact]
    public async Task AvailabilityAndHoursUseBothBuffersIncludingOngoingCleanup()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token);
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var path = $"/api/appointments/availability?serviceId={service.Id}&staffMemberId={member.Id}&date=2026-10-15";
        using var preview = await owner.GetAsync(path, Token); var slots = await Body<AppointmentEndpoints.AvailabilityResponse>(preview, HttpStatusCode.OK);
        Assert.DoesNotContain(new DateTime(2026, 10, 15, 6, 0, 0, DateTimeKind.Utc), slots.StartsAt);
        Assert.Contains(new DateTime(2026, 10, 15, 15, 30, 0, DateTimeKind.Utc), slots.StartsAt); Assert.DoesNotContain(new DateTime(2026, 10, 15, 15, 45, 0, DateTimeKind.Utc), slots.StartsAt);
        using var outside = await PostAsync(owner, "/api/appointments/", Request(service, member.Id, 9), csrf); Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
        using var first = await PostAsync(owner, "/api/appointments/", Request(service, member.Id, 10), csrf); var saved = await Body<AppointmentEndpoints.AppointmentResponse>(first, HttpStatusCode.Created);
        using var overlap = await PostAsync(owner, "/api/appointments/", Request(service, member.Id, 10, 30), csrf); Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        using var previewAfter = await owner.GetAsync(path, Token); var after = await Body<AppointmentEndpoints.AvailabilityResponse>(previewAfter, HttpStatusCode.OK);
        Assert.DoesNotContain(saved.StartsAt.AddMinutes(30), after.StartsAt); Assert.Contains(saved.StartsAt.AddMinutes(45), after.StartsAt);
        clock.Now = new DateTimeOffset(saved.EndsAt.AddMinutes(5));
        using var cleanupOwner = await InviteOwnerAsync(seed); var cleanupCsrf = await GetCsrfAsync(cleanupOwner);
        var days = Enumerable.Range(0, 7).Select(day => new WeeklyHours.DayRequest(day, false, "09:00", "10:20")).ToArray();
        var business = await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(Token);
        using var businessWrite = await PostAsync(cleanupOwner, "/api/business-hours/", new { version = business.Version, days }, cleanupCsrf); Assert.Equal(HttpStatusCode.Conflict, businessWrite.StatusCode);
        using var staffWrite = await PostAsync(cleanupOwner, $"/api/staff-members/{member.Id}/hours", new { version = member.Version, days }, cleanupCsrf); Assert.Equal(HttpStatusCode.Conflict, staffWrite.StatusCode);
        Assert.Contains("appointments_outside_hours", await staffWrite.Content.ReadAsStringAsync(Token)); Assert.Empty(await db.StaffHoursAudits.ToArrayAsync(Token)); Assert.Empty(await db.BusinessHoursAudits.ToArrayAsync(Token));
        var allowedDays = Enumerable.Range(0, 7).Select(day => new WeeklyHours.DayRequest(day, false, "09:00", "10:30")).ToArray();
        using var allowed = await PostAsync(cleanupOwner, $"/api/staff-members/{member.Id}/hours", new { version = member.Version, days = allowedDays }, cleanupCsrf); Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task TwentyParallelBookingsPersistOneExpandedBlock()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.BufferBeforeMinutes = 7; service.BufferAfterMinutes = 11; await db.SaveChangesAsync(Token);
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(owner, "/api/appointments/", Request(service, member.Id), csrf)));
        try { Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Created); Assert.Equal(19, responses.Count(item => item.StatusCode == HttpStatusCode.Conflict)); }
        finally { foreach (var response in responses) response.Dispose(); }
        var block = await db.AppointmentTimeBlocks.SingleAsync(Token); Assert.Equal(block.StartsAt.AddMinutes(-7), block.OccupiedStartsAt); Assert.Equal(block.EndsAt.AddMinutes(11), block.OccupiedEndsAt);
        Assert.Single(await db.Appointments.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task BufferWriterWaitingBeforeBookingForcesFreshServiceVersion()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); using var other = await AppointmentTests.OtherOwnerAsync(seed);
        var csrf = await GetCsrfAsync(owner); var otherCsrf = await GetCsrfAsync(other);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.AsNoTracking().SingleAsync(Token); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"ServiceDefinitions\" WHERE \"Id\" = @id FOR UPDATE", connection, transaction)) { command.Parameters.AddWithValue("id", service.Id); await command.ExecuteScalarAsync(Token); }
        var writer = PostAsync(other, $"/api/services/{service.Id}/buffers", new { version = service.Version, bufferBeforeMinutes = 5, bufferAfterMinutes = 10 }, otherCsrf);
        using var waitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(waitTimeout.Token) < 1) await Task.Delay(20, waitTimeout.Token);
        var booking = PostAsync(owner, "/api/appointments/", Request(service, member.Id), csrf);
        await AwaitInviteLocksAsync(app); await transaction.CommitAsync(Token);
        using var updated = await writer; Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var rejected = await booking; Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Single(await db.ServiceDefinitionAudits.ToArrayAsync(Token));
    }
}
