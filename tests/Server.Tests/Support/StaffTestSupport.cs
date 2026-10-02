using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.TestAccounts;

namespace Server.Tests.Support;

internal static class StaffTestSupport
{
    internal const string InvitePath = "/api/staff-invitations/";

    internal const string AcceptInvitePath = "/api/staff-invitations/accept";

    internal static async Task<StaffInvitationEndpoints.IssuedResponse> InviteAsync(HttpClient owner, string email = StaffEmail)
    {
        using var response = await PostAsync(owner, InvitePath, new { email, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<StaffInvitationEndpoints.IssuedResponse>(await response.Content.ReadFromJsonAsync<StaffInvitationEndpoints.IssuedResponse>(TestContext.Current.CancellationToken));
    }

    internal static Task<HttpResponseMessage> AcceptInviteAsync(HttpClient client, string token, string csrf, string email = StaffEmail, string password = Password) =>
        PostAsync(client, AcceptInvitePath, new { email, token, password, confirmPassword = password, role = "Owner" }, csrf);

    internal static async Task<Guid> CreatePasswordStaffAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var staff = new AppUser { Email = StaffEmail, UserName = StaffEmail, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(staff, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(staff, "Staff")).Succeeded);
        return staff.Id;
    }

    internal static async Task LoginPasswordStaffAsync(HttpClient client, string password = Password)
    {
        using var response = await PostAsync(client, "/api/auth/login",
            new { email = StaffEmail, password }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    internal static async Task AssertStaffPasswordAsync(WebApplicationFactory<Program> app, Guid id, string password)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var staff = await users.FindByIdAsync(id.ToString());
        Assert.NotNull(staff);
        Assert.True(await users.CheckPasswordAsync(staff, password));
        Assert.Equal(new[] { "Staff" }, await users.GetRolesAsync(staff));
        Assert.False(staff.TwoFactorEnabled);
    }
}
