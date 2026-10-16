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
public sealed class PublicBookingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static string Availability(Guid service, Guid member, string date = "2026-10-16") =>
        $"/api/public-booking/availability?serviceId={service}&staffMemberId={member}&date={date}";
    private static async Task<T> Read<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(Token));
    }
    private static async Task Fields(HttpClient client, string path, params string[] expected)
    {
        using var response = await client.GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(expected.Order(), json.RootElement.EnumerateObject().Select(item => item.Name).Order());
    }

    [Fact]
    public async Task CatalogIsAnonymousPagedActiveOnlyAndProjectsPublicFields()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.BusinessProfiles.SingleAsync(Token);
        profile.Name = "Sentetik işletme"; profile.Phone = "+905000000002"; profile.Address = "Sentetik adres"; profile.Email = "private@example.test";
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.Name = "Ortak ad";
        db.ServiceDefinitions.AddRange(Enumerable.Range(0, 3).Select(_ => new ServiceDefinition
        { Id = Guid.NewGuid(), Name = "Ortak ad", DurationMinutes = 30, Price = 100m, Currency = "TRY", Version = Guid.NewGuid() }));
        db.ServiceDefinitions.AddRange(new ServiceDefinition
        { Id = Guid.NewGuid(), Name = "Pasif özel ad", DurationMinutes = 30, Price = 100m, Currency = "TRY", Version = Guid.NewGuid(), IsActive = false },
            new ServiceDefinition
            { Id = Guid.NewGuid(), Name = "Silinmiş özel ad", DurationMinutes = 30, Price = 100m, Currency = "TRY", Version = Guid.NewGuid(), IsDeleted = true, IsActive = false });
        await db.SaveChangesAsync(Token);
        var first = await Read<PublicBookingEndpoints.CatalogResponse>(client, "/api/public-booking/catalog?pageSize=2");
        var second = await Read<PublicBookingEndpoints.CatalogResponse>(client, "/api/public-booking/catalog?page=2&pageSize=2");
        var last = await Read<PublicBookingEndpoints.CatalogResponse>(client, "/api/public-booking/catalog?page=10000&pageSize=2");
        Assert.Equal(4, first.TotalCount); Assert.Equal(1, first.Page); Assert.True(first.HasMore); Assert.False(second.HasMore); Assert.Equal(2, last.Page);
        Assert.Equal(second.Items.Select(item => item.Id), last.Items.Select(item => item.Id));
        var ids = first.Items.Concat(second.Items).Select(item => item.Id).ToArray();
        Assert.Equal(4, ids.Distinct().Count());
        Assert.Equal(await db.ServiceDefinitions.Where(item => item.IsActive && !item.IsDeleted).OrderBy(item => item.Name).ThenBy(item => item.Id).Select(item => item.Id).ToArrayAsync(Token), ids);
        Assert.Equal("123.45", first.Items.Concat(second.Items).Single(item => item.Id == service.Id).Price);
        Assert.Equal(new PublicBookingEndpoints.BusinessResponse(profile.Name, profile.Phone, profile.Address), first.Business);
        Assert.Equal("Europe/Istanbul", first.Policy.TimeZone); Assert.Equal((await db.BookingPolicies.SingleAsync(Token)).Version, first.Policy.Version);
        using var raw = await client.GetAsync("/api/public-booking/catalog", Token);
        using var json = JsonDocument.Parse(await raw.Content.ReadAsStringAsync(Token));
        Assert.Equal(new[] { "name", "phone", "address" }.Order(), json.RootElement.GetProperty("business").EnumerateObject().Select(item => item.Name).Order());
        Assert.Equal(new[] { "id", "name", "durationMinutes", "price", "currency", "version" }.Order(), json.RootElement.GetProperty("items")[0].EnumerateObject().Select(item => item.Name).Order());
        Assert.DoesNotContain("private@example.test", json.RootElement.ToString());
        Assert.DoesNotContain("Pasif özel ad", json.RootElement.ToString()); Assert.DoesNotContain("Silinmiş özel ad", json.RootElement.ToString());
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.BookingPolicyAudits.ToArrayAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task StaffListFiltersAssignmentActiveAndDeletedAndPagesWithoutLeakingAccountData()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token);
        foreach (var state in new[] { "inactive", "deleted", "unassigned" })
        {
            var member = new StaffMember { Id = Guid.NewGuid(), Name = state, Version = Guid.NewGuid(), IsActive = state is not ("inactive" or "deleted"), IsDeleted = state == "deleted" };
            db.StaffMembers.Add(member);
            if (state != "unassigned") db.StaffServiceAssignments.Add(new() { StaffMemberId = member.Id, ServiceDefinitionId = service.Id });
        }
        await db.SaveChangesAsync(Token);
        var path = $"/api/public-booking/staff?serviceId={service.Id}&pageSize=1";
        var first = await Read<PublicBookingEndpoints.StaffPage>(client, path);
        var second = await Read<PublicBookingEndpoints.StaffPage>(client, path + "&page=2");
        var last = await Read<PublicBookingEndpoints.StaffPage>(client, path + "&page=10000");
        Assert.Equal(2, first.TotalCount); Assert.True(first.HasMore); Assert.False(second.HasMore);
        Assert.NotEqual(Assert.Single(first.Items).Id, Assert.Single(second.Items).Id); Assert.Equal(second.Items, last.Items);
        using var response = await client.GetAsync(path, Token); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(new[] { "id", "name" }, json.RootElement.GetProperty("items")[0].EnumerateObject().Select(item => item.Name));
        db.StaffServiceAssignments.RemoveRange(await db.StaffServiceAssignments.ToArrayAsync(Token)); await db.SaveChangesAsync(Token);
        var empty = await Read<PublicBookingEndpoints.StaffPage>(client, path); Assert.Empty(empty.Items); Assert.Equal(0, empty.TotalCount);
        service.IsActive = false; await db.SaveChangesAsync(Token);
        using var hidden = await client.GetAsync(path, Token); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task AvailabilityMatchesOwnerRulesWithBuffersBreaksLeaveExceptionsAndOccupiedTime()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var client = app.CreateClient(); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        service.DurationMinutes = 25; service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10;
        var policy = await db.BookingPolicies.SingleAsync(Token); policy.LeadMinutes = 120; policy.HorizonDays = 7; policy.SlotMinutes = 5;
        db.BusinessDateExceptions.Add(new() { ScheduleId = 1, Date = new(2026, 10, 16), OpensAtMinute = 540, ClosesAtMinute = 720 });
        db.StaffDateExceptions.Add(new() { StaffMemberId = member.Id, Date = new(2026, 10, 16), OpensAtMinute = 540, ClosesAtMinute = 720 });
        db.BusinessWeeklyBreaks.Add(new() { ScheduleId = 1, Day = 4, Position = 1, StartsAtMinute = 600, EndsAtMinute = 615 });
        db.BusinessDateBreaks.Add(new() { ScheduleId = 1, Date = new(2026, 10, 16), Position = 1, StartsAtMinute = 600, EndsAtMinute = 615 });
        db.StaffLeaves.Add(new() { Id = Guid.NewGuid(), StaffMemberId = member.Id, StartsAt = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc), EndsAt = new(2026, 10, 16, 8, 15, 0, DateTimeKind.Utc), RequestHash = new string('A', 64) });
        await db.SaveChangesAsync(Token);
        using var create = await PostAsync(owner, "/api/appointments/", new AppointmentEndpoints.CreateRequest(Guid.NewGuid(), service.Id, service.Version, member.Id,
            "2026-10-16T09:10:00+03:00", "Gizli sentetik müşteri", "05000000001"), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var appointment = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await create.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        var path = Availability(service.Id, member.Id);
        var result = await Read<PublicBookingEndpoints.AvailabilityResponse>(client, path);
        var management = await Read<AppointmentEndpoints.AvailabilityResponse>(owner, path.Replace("public-booking", "appointments"));
        Assert.NotEmpty(result.StartsAt); Assert.Equal(management.StartsAt, result.StartsAt);
        Assert.Equal(service.Version, result.ServiceVersion); Assert.Equal(policy.Version, result.PolicyVersion);
        Assert.All(result.StartsAt, start => Assert.Equal(DateTimeKind.Utc, start.Kind));
        Assert.DoesNotContain(appointment.StartsAt, result.StartsAt);
        Assert.Contains(new DateTime(2026, 10, 16, 8, 20, 0, DateTimeKind.Utc), result.StartsAt);
        Assert.DoesNotContain(new DateTime(2026, 10, 16, 6, 40, 0, DateTimeKind.Utc), result.StartsAt);
        Assert.DoesNotContain(new DateTime(2026, 10, 16, 7, 0, 0, DateTimeKind.Utc), result.StartsAt);
        await Fields(client, path, "timeZone", "serviceVersion", "policyVersion", "startsAt");
        using var cancel = await PostAsync(owner, $"/api/appointments/{appointment.Id}/cancel", new { appointment.Version, RequestKey = Guid.NewGuid() }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Contains(appointment.StartsAt, (await Read<PublicBookingEndpoints.AvailabilityResponse>(client, path)).StartsAt);
        Assert.Single(await db.Appointments.ToArrayAsync(Token));
        Assert.Equal(new[] { "Cancelled", "Created" }, await db.AppointmentAudits.OrderBy(item => item.Kind).Select(item => item.Kind).ToArrayAsync(Token));
    }

    [Fact]
    public async Task PublicEmptySlotsDoNotRevealTheReasonAndPrivateDefinitionsReturn404()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var path = Availability(service.Id, member.Id);
        (await db.BusinessHoursSchedules.SingleAsync(Token)).IsConfigured = false; await db.SaveChangesAsync(Token);
        Assert.Empty((await Read<PublicBookingEndpoints.AvailabilityResponse>(client, path)).StartsAt);
        await Fields(client, path, "timeZone", "serviceVersion", "policyVersion", "startsAt");
        foreach (var state in new[] { "inactive-service", "deleted-service", "inactive-staff", "deleted-staff", "unassigned" })
        {
            service.IsActive = state is not ("inactive-service" or "deleted-service"); service.IsDeleted = state == "deleted-service";
            member.IsActive = state is not ("inactive-staff" or "deleted-staff"); member.IsDeleted = state == "deleted-staff";
            if (state == "unassigned") db.StaffServiceAssignments.Remove(await db.StaffServiceAssignments.SingleAsync(item => item.StaffMemberId == member.Id, Token));
            await db.SaveChangesAsync(Token);
            using var response = await client.GetAsync(path, Token); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        using var missing = await client.GetAsync(Availability(Guid.NewGuid(), member.Id), Token); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ValidatesUntrustedQueryAndUsesIstanbulDayLeadTimeAndHorizon()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var clock = new Clock { Now = new(2026, 10, 15, 21, 30, 0, TimeSpan.Zero) };
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var policy = await db.BookingPolicies.SingleAsync(Token); policy.LeadMinutes = 600; policy.HorizonDays = 2; policy.SlotMinutes = 15;
        await db.SaveChangesAsync(Token);
        var result = await Read<PublicBookingEndpoints.AvailabilityResponse>(client, Availability(service.Id, member.Id));
        Assert.Equal(new DateTime(2026, 10, 16, 7, 30, 0, DateTimeKind.Utc), result.StartsAt[0]);
        await Read<PublicBookingEndpoints.AvailabilityResponse>(client, Availability(service.Id, member.Id, "2026-10-18"));
        foreach (var date in new[] { "2026-10-15", "2026-10-19", "0001-01-01", "9999-12-31", "2026-02-30", "16.10.2026", "2026-10-16T00:00:00Z", "" })
        { using var response = await client.GetAsync(Availability(service.Id, member.Id, date), Token); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        foreach (var suffix in new[] { "page=0", "page=10001", "pageSize=0", "pageSize=51", "page=oops" })
        {
            using var catalog = await client.GetAsync("/api/public-booking/catalog?" + suffix, Token); Assert.Equal(HttpStatusCode.BadRequest, catalog.StatusCode);
            using var staff = await client.GetAsync($"/api/public-booking/staff?serviceId={service.Id}&{suffix}", Token); Assert.Equal(HttpStatusCode.BadRequest, staff.StatusCode);
        }
        foreach (var path in new[] { "/api/public-booking/staff", "/api/public-booking/staff?serviceId=oops", $"/api/public-booking/staff?serviceId={Guid.Empty}", Availability(Guid.Empty, member.Id), Availability(service.Id, Guid.Empty), "/api/public-booking/availability" })
        { using var response = await client.GetAsync(path, Token); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        Assert.Empty(await db.Appointments.ToArrayAsync(Token));
    }

    [Fact]
    public async Task EmptyCatalogAndStaffRemainEmptyAndPublicRoutesDoNotAllowWritesOrWeakenOwnerProtection()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        using var client = app.CreateClient();
        var empty = await Read<PublicBookingEndpoints.CatalogResponse>(client, "/api/public-booking/catalog?page=10000");
        Assert.Empty(empty.Items); Assert.Equal(0, empty.TotalCount); Assert.Equal(1, empty.Page); Assert.False(empty.HasMore);
        foreach (var path in new[] { "/api/public-booking/catalog", "/api/public-booking/staff", "/api/public-booking/availability", "/api/public-booking/appointments", "/api/public-booking/verification/request" })
        { using var response = await client.PostAsJsonAsync(path, new { }, Token); Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed }); }
        foreach (var path in new[] { "/api/services/", "/api/staff-members/", "/api/business-profile/", "/api/booking-policy/", "/api/appointments/", $"/api/appointments/{Guid.NewGuid()}" })
        { using var response = await client.GetAsync(path, Token); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
        using var write = await client.PostAsJsonAsync("/api/appointments/", new { }, Token); Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentAudits.ToArrayAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task RateLimitIsSharedByPublicReadsAndCannotBeBypassedWithForwardedHeaders()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        using var client = app.CreateClient();
        for (var index = 0; index < 60; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, index % 2 == 0 ? "/api/public-booking/catalog" : $"/api/public-booking/staff?serviceId={Guid.Empty}");
            request.Headers.Add("X-Forwarded-For", $"192.0.2.{index + 1}");
            using var response = await client.SendAsync(request, Token);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        using var limited = await client.GetAsync("/api/public-booking/catalog", Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); Assert.True(limited.Headers.CacheControl?.NoStore);
        Assert.InRange(Assert.IsType<TimeSpan>(limited.Headers.RetryAfter?.Delta).TotalSeconds, 1, 60);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        using var health = await client.GetAsync("/health/live", Token); Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task GlobalLimitAlsoGivesPublicClientsRetryAfterWithoutChangingOtherResponses()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        using var client = app.CreateClient();
        for (var index = 0; index < 600; index++)
        { using var response = await client.GetAsync("/health/live", Token); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
        using var limited = await client.GetAsync("/api/public-booking/catalog", Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); Assert.True(limited.Headers.CacheControl?.NoStore);
        Assert.InRange(Assert.IsType<TimeSpan>(limited.Headers.RetryAfter?.Delta).TotalSeconds, 1, 60);
        using var health = await client.GetAsync("/health/live", Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, health.StatusCode); Assert.Empty(await health.Content.ReadAsStringAsync(Token));
    }
}
