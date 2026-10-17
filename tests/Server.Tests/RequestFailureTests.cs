using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.PasswordTestSupport;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class RequestFailureTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CsrfRejectionsHaveStableCodeAndNeverRunMutations()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed);
        var id = Guid.NewGuid();
        object command = new { version = id, requestKey = id };
        object hours = new { version = id, days = Array.Empty<object>() };
        var requests = new (string Path, object Body)[]
        {
            ("/api/services/", new { id, name = "Sentetik", durationMinutes = 25, price = "450.00" }),
            ($"/api/services/{id}/status", new { version = id, isActive = false }),
            ($"/api/services/{id}/delete", command),
            ($"/api/services/{id}/buffers", new { version = id, beforeMinutes = 0, afterMinutes = 0 }),
            ("/api/staff-members/", new { id, name = "Sentetik" }),
            ($"/api/staff-members/{id}", new { version = id, name = "Sentetik" }),
            ($"/api/staff-members/{id}/status", new { version = id, isActive = false }),
            ($"/api/staff-members/{id}/delete", command),
            ($"/api/staff-members/{id}/services", new { version = id, services = Array.Empty<object>() }),
            ($"/api/staff-members/{id}/hours", hours),
            ("/api/business-hours/", hours),
            ("/api/business-hours/breaks", new { version = id, breaks = Array.Empty<object>() }),
            ("/api/business-profile/", new { version = id, name = "Sentetik", phone = (string?)null, email = (string?)null, address = (string?)null }),
            ("/api/booking-policy/", new { version = id, leadMinutes = 0, horizonDays = 15, slotMinutes = 15 }),
            ("/api/appointments/", new { requestKey = id, serviceId = id, serviceVersion = id, staffMemberId = id, startsAt = "2026-10-18T10:00:00+03:00", customerName = "Sentetik", customerPhone = "05000000001" }),
            ($"/api/appointments/{id}/move", new { version = id, requestKey = id, staffMemberId = id, startsAt = "2026-10-18T10:00:00+03:00" }),
            ($"/api/appointments/{id}/cancel", command),
            ("/api/auth/change-password", new { currentPassword = "Synthetic!Owner123", newPassword = "Synthetic!Next123", confirmPassword = "Synthetic!Next123" }),
            ("/api/auth/recovery-email/request", new { }),
            ("/api/auth/mfa/recovery-codes", new { currentPassword = "Synthetic!Owner123" }),
            ("/api/staff-invitations/", new { email = "staff@example.test", verifiedRecipient = true }),
            ("/api/staff-password-resets/", new { email = "staff@example.test", verifiedRecipient = true }),
            ($"/api/staff-accounts/{id}/deactivate", command)
        };
        foreach (var request in requests)
        {
            using var response = await PostAsync(owner, request.Path, request.Body, null);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
            Assert.Equal("csrf_invalid", problem.GetProperty("code").GetString());
            Assert.Equal("Geçersiz istek doğrulaması.", problem.GetProperty("title").GetString());
        }
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.StaffMembers.AnyAsync(Token));
        Assert.False(await db.ServiceDefinitions.AnyAsync(Token));
        Assert.False(await db.Appointments.AnyAsync(Token));
        Assert.Equal(string.Empty, await db.BusinessProfiles.Select(item => item.Name).SingleAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task ValidationStaysOnFieldsWhileUnauthorizedAndForbiddenRemainDistinct()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        using var anonymous = app.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync("/api/business-profile/", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        await CreatePasswordStaffAsync(app);
        using var staff = app.CreateClient();
        await LoginPasswordStaffAsync(staff);
        using var forbidden = await staff.GetAsync("/api/business-profile/", Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var owner = await InviteOwnerAsync(seed);
        var csrf = await GetCsrfAsync(owner);
        using var invalidName = await PostAsync(owner, "/api/staff-members/", new { id = Guid.NewGuid(), name = " " }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, invalidName.StatusCode);
        var problem = await invalidName.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.False(problem.TryGetProperty("code", out _));
        Assert.NotEmpty(problem.GetProperty("errors").GetProperty("name").EnumerateArray());
        using var profile = await owner.GetAsync("/api/business-profile/", Token);
        var current = await profile.Content.ReadFromJsonAsync<JsonElement>(Token);
        using var invalidPhone = await PostAsync(owner, "/api/business-profile/", new { version = current.GetProperty("version").GetGuid(), name = "Sentetik", phone = "invalid", email = (string?)null, address = (string?)null }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPhone.StatusCode);
        var fields = await invalidPhone.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.NotEmpty(fields.GetProperty("errors").GetProperty("phone").EnumerateArray());
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(string.Empty, await db.BusinessProfiles.Select(item => item.Name).SingleAsync(Token));
    }
}
