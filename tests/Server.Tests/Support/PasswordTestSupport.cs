using Microsoft.AspNetCore.Mvc.Testing;
using Server.Features.Identity;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.TestAccounts;

namespace Server.Tests.Support;

internal static class PasswordTestSupport
{
    internal const string PasswordPath = "/api/auth/change-password";

    internal static async Task<HttpResponseMessage> ChangePasswordAsync(
        HttpClient client, string currentPassword, string newPassword, string confirmPassword) =>
        await PostAsync(client, PasswordPath, new { currentPassword, newPassword, confirmPassword },
            await GetCsrfAsync(client));

    internal const string ResetPath = "/api/auth/reset-password";

    internal static IssueOwnerPasswordReset.IssueRequest ResetIssue(RecoverySeed seed, string reference) =>
        new(RecoveryInstance, seed.OwnerId, "operator-01", reference);

    internal static async Task<IssueOwnerPasswordReset.IssuedToken> IssueResetAsync(
        WebApplicationFactory<Program> app, IssueOwnerPasswordReset.IssueRequest request) =>
        Assert.IsType<IssueOwnerPasswordReset.IssuedToken>(await IssueOwnerPasswordReset.IssueAsync(
            app.Services, request, TestContext.Current.CancellationToken));

    internal static async Task<HttpResponseMessage> ResetAsync(HttpClient client, string token,
        string password = NewPassword, string? confirm = null) => await PostAsync(client, ResetPath,
            new { token, newPassword = password, confirmPassword = confirm ?? password }, await GetCsrfAsync(client));

    internal sealed class ResetClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
