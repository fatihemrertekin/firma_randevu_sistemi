using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Server.Features.Identity;

public static class IdentityCommandExtensions
{
    public static async Task<bool> TryRunIdentityCommandAsync(this WebApplication app, string[] args)
    {
        if (args is ["bootstrap-owner"])
        {
            Environment.ExitCode = await BootstrapOwner.RunAsync(app.Services, CancellationToken.None);
            return true;
        }

        if (args is ["recover-owner-mfa"])
        {
            try
            {
                Environment.ExitCode = await RecoverOwnerMfa.RunAsync(app.Services, CancellationToken.None);
            }
            catch (Exception exception) when (exception is DbUpdateException or NpgsqlException or OperationCanceledException)
            {
                // Kurtarma hatasında DB ayrıntılarını ve kimlik bilgilerini konsola yazmadan dur.
                Console.Error.WriteLine("Kurtarma sonucu doğrulanamadı; tekrar denemeden önce işlem kaydını kontrol edin.");
                Environment.ExitCode = 1;
            }
            return true;
        }

        if (args is ["issue-owner-password-reset"])
        {
            try
            {
                Environment.ExitCode = await IssueOwnerPasswordReset.RunAsync(app.Services, CancellationToken.None);
            }
            catch (Exception exception) when (exception is DbUpdateException or NpgsqlException or OperationCanceledException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Token teslimi doğrulanamadı; özel dosyayı ve işlem kaydını kontrol edin. Aynı referansla otomatik tekrar yapmayın.");
                Environment.ExitCode = 1;
            }
            return true;
        }

        return false;
    }
}
