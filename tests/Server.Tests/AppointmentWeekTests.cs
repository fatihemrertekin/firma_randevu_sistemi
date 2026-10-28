using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
public sealed class AppointmentWeekTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class DayClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 15, 21, 30, 0, TimeSpan.Zero);
    }

    private static Guid Add(AppDbContext db, Guid owner, Guid member, Guid service, DateTime start,
        string status = "Cancelled", Guid? id = null)
    {
        var appointment = new Appointment
        {
            Id = id ?? Guid.NewGuid(),
            ActorId = owner,
            RequestKey = Guid.NewGuid(),
            RequestHash = new string('A', 64),
            CustomerName = "Haftalık sentetik müşteri",
            CustomerPhone = "+905000000001",
            CreatedAt = start.AddDays(-1),
            Version = Guid.NewGuid()
        };
        db.Appointments.Add(appointment);
        db.AppointmentTimeBlocks.Add(new AppointmentTimeBlock
        {
            AppointmentId = appointment.Id,
            StaffMemberId = member,
            ServiceDefinitionId = service,
            StaffName = "Tarihsel personel",
            ServiceName = "Tarihsel hizmet",
            DurationMinutes = 1,
            Price = 123.45m,
            Currency = "TRY",
            StartsAt = start,
            EndsAt = start.AddMinutes(1),
            OccupiedStartsAt = start,
            OccupiedEndsAt = start.AddMinutes(1),
            Status = status,
            PendingExpiresAt = status == "Pending" ? start.AddMinutes(-1) : null
        });
        return appointment.Id;
    }

    private static async Task<AppointmentListEndpoints.AppointmentWeekPage> Read(HttpClient owner, string query)
    {
        using var response = await owner.GetAsync("/api/appointments/week" + query, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<AppointmentListEndpoints.AppointmentWeekPage>(await response.Content.ReadFromJsonAsync<AppointmentListEndpoints.AppointmentWeekPage>(Token));
    }

    [Fact]
    public async Task IstanbulWeekCrossesYearWithAllSevenCountsAndOnlyTheSelectedDaysHistoricalRows()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new DayClock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var members = await db.StaffMembers.OrderBy(item => item.Id).ToArrayAsync(Token);
        var service = await db.ServiceDefinitions.SingleAsync(Token);
        var monday = new DateTime(2026, 12, 27, 21, 0, 0, DateTimeKind.Utc);
        Add(db, seed.OwnerId, members[0].Id, service.Id, monday.AddMinutes(-1));
        for (var index = 0; index < 7; index++) Add(db, seed.OwnerId, members[0].Id, service.Id, monday.AddDays(index));
        Add(db, seed.OwnerId, members[0].Id, service.Id, monday.AddDays(7));
        var selected = new List<Guid>();
        foreach (var (status, minute) in new[] { ("Confirmed", 1), ("Pending", 2), ("Completed", 3), ("NoShow", 4), ("Cancelled", 5) })
            selected.Add(Add(db, seed.OwnerId, members[0].Id, service.Id, monday.AddDays(2).AddMinutes(minute), status));
        Add(db, seed.OwnerId, members[1].Id, service.Id, monday.AddDays(2).AddMinutes(6));
        members[0].IsActive = false; members[0].IsDeleted = true; service.IsActive = false; service.IsDeleted = true;
        await db.SaveChangesAsync(Token);
        var count = await db.Appointments.CountAsync(Token); var audits = await db.AppointmentAudits.CountAsync(Token);
        var value = await Read(owner, "?date=2026-12-30");
        Assert.Equal("2026-12-28", value.WeekStart); Assert.Equal("2027-01-04", value.WeekEndExclusive);
        Assert.Equal("2026-12-30", value.Date); Assert.Equal("Europe/Istanbul", value.TimeZone); Assert.Equal(10, value.PageSize);
        Assert.Equal(new[] { 1, 1, 7, 1, 1, 1, 1 }, value.Days.Select(day => day.TotalCount));
        Assert.Equal(Enumerable.Range(0, 7).Select(index => new DateOnly(2026, 12, 28).AddDays(index).ToString("yyyy-MM-dd")), value.Days.Select(day => day.Date));
        Assert.Equal(7, value.TotalCount); Assert.Equal(value.TotalCount, value.Days[2].TotalCount);
        Assert.All(value.Items, row => Assert.Equal("2026-12-30", DateOnly.FromDateTime(row.StartsAt.AddHours(3)).ToString("yyyy-MM-dd")));
        Assert.All(value.Items, row => { Assert.Equal("Tarihsel personel", row.StaffName); Assert.Equal("Tarihsel hizmet", row.ServiceName); });
        var filtered = await Read(owner, $"?date=2026-12-30&staffMemberId={members[0].Id}");
        Assert.Equal(6, filtered.TotalCount); Assert.Equal(6, filtered.Days[2].TotalCount);
        Assert.Subset(filtered.Items.Select(row => row.Id).ToHashSet(), selected.ToHashSet());
        using var response = await owner.GetAsync("/api/appointments/week?date=2026-12-30", Token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(10, json.RootElement.EnumerateObject().Count());
        var row = json.RootElement.GetProperty("items")[0];
        foreach (var secret in new[] { "customerPhone", "requestKey", "requestHash", "actorId", "email" }) Assert.False(row.TryGetProperty(secret, out _));
        Assert.Equal(count, await db.Appointments.CountAsync(Token)); Assert.Equal(audits, await db.AppointmentAudits.CountAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task SelectedDayPaginationIsStableBoundedAndClampedWithoutPagingTheWeekCounts()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new DayClock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        var ids = Enumerable.Range(1, 65).Select(index => Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}")).ToArray();
        foreach (var id in ids.Reverse()) Add(db, seed.OwnerId, member.Id, service.Id, new DateTime(2026, 10, 16, 6, 0, 0, DateTimeKind.Utc), id: id);
        Add(db, seed.OwnerId, member.Id, service.Id, new DateTime(2026, 10, 17, 6, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync(Token);
        var first = await Read(owner, "?date=2026-10-16&pageSize=50");
        var last = await Read(owner, "?date=2026-10-16&pageSize=50&page=10000");
        Assert.Equal(50, first.Items.Length); Assert.Equal(15, last.Items.Length); Assert.Equal(2, last.Page);
        Assert.True(first.HasMore); Assert.False(last.HasMore); Assert.Equal(ids, first.Items.Concat(last.Items).Select(row => row.Id));
        Assert.Equal(65, first.TotalCount); Assert.Equal(first.Days, last.Days); Assert.Equal(65, first.Days[4].TotalCount); Assert.Equal(1, first.Days[5].TotalCount);
        foreach (var size in new[] { 10, 20 })
        { var page = await Read(owner, $"?date=2026-10-16&pageSize={size}"); Assert.Equal(size, page.Items.Length); Assert.Equal(first.Days, page.Days); }
        var emptyDay = await Read(owner, "?date=2026-10-18&page=10000");
        Assert.Empty(emptyDay.Items); Assert.Equal(0, emptyDay.TotalCount); Assert.Equal(1, emptyDay.Page); Assert.Equal(first.Days, emptyDay.Days);
    }

    [Fact]
    public async Task EmptyWeekValidationOwnerMfaAndSharedReadLimitDoNotWriteBusinessData()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new DayClock()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed);
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        { using var response = await client.GetAsync("/api/appointments/week", Token); Assert.Equal(status, response.StatusCode); }
        foreach (var query in new[] { "date=", "date=2026-02-30", "date=0001-01-02", "date=9999-12-30", "date=2026-13-01", "staffMemberId=wrong",
            "staffMemberId=00000000-0000-0000-0000-000000000000", "page=0", "page=10001", "pageSize=0", "pageSize=51" })
        { using var response = await owner.GetAsync("/api/appointments/week?" + query, Token); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        var empty = await Read(owner, "");
        Assert.Equal("2026-10-16", empty.Date); Assert.Equal("2026-10-12", empty.WeekStart);
        Assert.Equal(7, empty.Days.Length); Assert.All(empty.Days, day => Assert.Equal(0, day.TotalCount)); Assert.Empty(empty.Items);
        var limited = false;
        for (var index = 0; index < 61; index++)
        {
            using var response = await owner.GetAsync(index % 2 == 0 ? "/api/appointments/week" : "/api/appointments/", Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            { Assert.InRange(Assert.IsType<TimeSpan>(response.Headers.RetryAfter?.Delta).TotalSeconds, 1, 60); limited = true; break; }
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.True(limited);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentAudits.ToArrayAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }
}
