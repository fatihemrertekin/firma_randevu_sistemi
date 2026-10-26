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
public sealed class AppointmentListTests
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
            CustomerName = "Sentetik müşteri",
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
            StaffName = "Eski personel adı",
            ServiceName = "Eski hizmet adı",
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

    private static async Task<AppointmentListEndpoints.AppointmentPage> Read(HttpClient owner, string query)
    {
        using var response = await owner.GetAsync("/api/appointments/" + query, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<AppointmentListEndpoints.AppointmentPage>(await response.Content.ReadFromJsonAsync<AppointmentListEndpoints.AppointmentPage>(Token));
    }

    [Fact]
    public async Task IstanbulDayIncludesMidnightAndAllStatusesButOmitsPhoneAndRetainsSnapshots()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new DayClock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var members = await db.StaffMembers.OrderBy(item => item.Id).ToArrayAsync(Token);
        var service = await db.ServiceDefinitions.SingleAsync(Token);
        var first = new DateTime(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);
        Add(db, seed.OwnerId, members[0].Id, service.Id, first.AddMinutes(-1));
        var included = new List<Guid>();
        foreach (var (status, offset) in new[] { ("Confirmed", 0), ("Pending", 1), ("Completed", 2), ("NoShow", 3), ("Cancelled", 1439) })
            included.Add(Add(db, seed.OwnerId, members[0].Id, service.Id, first.AddMinutes(offset), status));
        Add(db, seed.OwnerId, members[0].Id, service.Id, first.AddDays(1));
        members[0].IsActive = false; members[0].IsDeleted = true; members[0].Name = "Değişen personel";
        service.IsActive = false; service.IsDeleted = true; service.Name = "Değişen hizmet";
        await db.SaveChangesAsync(Token);
        var page = await Read(owner, "?date=2026-10-16");
        Assert.Equal(included, page.Items.Select(item => item.Id)); Assert.Equal(5, page.TotalCount);
        Assert.Equal("2026-10-16", page.Date); Assert.Equal("Europe/Istanbul", page.TimeZone); Assert.False(page.HasMore);
        Assert.All(page.Items, item => { Assert.Equal("Eski personel adı", item.StaffName); Assert.Equal("Eski hizmet adı", item.ServiceName); });
        Assert.Equal(page.Items.Select(item => item.Id), (await Read(owner, "")).Items.Select(item => item.Id));
        Assert.Equal(page.Items.Select(item => item.Id), (await Read(owner, $"?date=2026-10-16&staffMemberId={members[0].Id}")).Items.Select(item => item.Id));
        Assert.Empty((await Read(owner, $"?date=2026-10-16&staffMemberId={members[1].Id}")).Items);
        using var response = await owner.GetAsync("/api/appointments/?date=2026-10-16", Token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var row = json.RootElement.GetProperty("items")[0];
        Assert.False(row.TryGetProperty("customerPhone", out _)); Assert.False(row.TryGetProperty("requestHash", out _));
        Assert.False(row.TryGetProperty("actorId", out _)); Assert.False(row.TryGetProperty("requestKey", out _));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task EqualTimesAreStableAcrossPagesAndUnknownPageClampsToLastWithoutDroppingRows()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new DayClock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        var ids = Enumerable.Range(1, 23).Reverse().Select(index => Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}")).ToArray();
        foreach (var id in ids) Add(db, seed.OwnerId, member.Id, service.Id, new DateTime(2026, 10, 16, 6, 0, 0, DateTimeKind.Utc), id: id);
        await db.SaveChangesAsync(Token);
        var first = await Read(owner, "?date=2026-10-16&pageSize=10");
        var second = await Read(owner, "?date=2026-10-16&pageSize=10&page=2");
        var last = await Read(owner, "?date=2026-10-16&pageSize=10&page=10000");
        Assert.Equal(23, first.TotalCount); Assert.Equal(10, first.Items.Length); Assert.Equal(10, second.Items.Length);
        Assert.True(first.HasMore); Assert.True(second.HasMore); Assert.False(last.HasMore); Assert.Equal(3, last.Page);
        Assert.Equal(ids.Reverse(), first.Items.Concat(second.Items).Concat(last.Items).Select(item => item.Id));
        Assert.Equal(first.Items.Select(item => item.Id), (await Read(owner, "?date=2026-10-16&pageSize=10")).Items.Select(item => item.Id));
        var empty = await Read(owner, "?date=2026-10-17&page=10000");
        Assert.Empty(empty.Items); Assert.Equal(1, empty.Page); Assert.Equal(0, empty.TotalCount); Assert.False(empty.HasMore);
    }

    [Fact]
    public async Task ValidationAndOwnerMfaAndRateLimitProtectTheListWithoutWritingBusinessData()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new DayClock()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed);
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        { using var response = await client.GetAsync("/api/appointments/?date=2026-10-16", Token); Assert.Equal(status, response.StatusCode); }
        foreach (var query in new[] { "date=", "date=2026-02-30", "date=16.10.2026", "date=0001-01-01", "date=9999-12-31",
            "staffMemberId=wrong", "staffMemberId=00000000-0000-0000-0000-000000000000", "page=0", "page=10001", "pageSize=0", "pageSize=51" })
        { using var response = await owner.GetAsync("/api/appointments/?" + query, Token); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        Assert.Empty((await Read(owner, $"?staffMemberId={Guid.NewGuid()}")).Items);
        var limited = false;
        for (var index = 0; index < 61; index++)
        {
            using var response = await owner.GetAsync("/api/appointments/", Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.InRange(Assert.IsType<TimeSpan>(response.Headers.RetryAfter?.Delta).TotalSeconds, 1, 60);
                Assert.Empty(await response.Content.ReadAsStringAsync(Token));
                limited = true; break;
            }
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.True(limited);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentAudits.ToArrayAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }
}
