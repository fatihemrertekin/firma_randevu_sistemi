using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static partial class RecoverOwnerMfa
{
    public sealed record RecoveryRequest(
        string InstanceId, Guid OwnerId, string OperatorReference, string RequestReference);

    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var instanceId = Environment.GetEnvironmentVariable("APP_RECOVERY_INSTANCE_ID") ?? string.Empty;
        var ownerId = Environment.GetEnvironmentVariable("APP_RECOVERY_OWNER_ID");
        var operatorReference = Environment.GetEnvironmentVariable("APP_RECOVERY_OPERATOR_REF") ?? string.Empty;
        var requestReference = Environment.GetEnvironmentVariable("APP_RECOVERY_REQUEST_REF") ?? string.Empty;
        if (!Guid.TryParse(ownerId, out var parsedOwnerId) ||
            Environment.GetEnvironmentVariable("APP_RECOVERY_CONFIRM") != $"{instanceId}/{parsedOwnerId}")
        {
            Console.Error.WriteLine("Firma ve Owner kimliği için açık kurtarma onayı gerekli.");
            return 1;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var result = await RecoverAsync(services,
            new RecoveryRequest(instanceId, parsedOwnerId, operatorReference, requestReference), timeout.Token);
        if (!result)
        {
            Console.Error.WriteLine("Kurtarma reddedildi; hedefi, MFA durumunu ve işlem referansını kontrol edin.");
            return 1;
        }
        Console.WriteLine("MFA kurtarma kaydedildi. Owner mevcut parolasıyla giriş yapıp MFA'yı yeniden kurmalı.");
        return 0;
    }

    public static async Task<bool> RecoverAsync(
        IServiceProvider services, RecoveryRequest request, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (string.IsNullOrWhiteSpace(request.InstanceId) || request.InstanceId.Length > 128 ||
            request.InstanceId != configuration["Auth:InstanceId"] || request.OwnerId == Guid.Empty ||
            !ReferencePattern().IsMatch(request.OperatorReference) ||
            !ReferencePattern().IsMatch(request.RequestReference))
        {
            return false;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Lock before reading Identity state so two recovery commands cannot reset one account twice.
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {request.OwnerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || !user.TwoFactorEnabled || !await users.IsInRoleAsync(user, "Owner") ||
            await db.OwnerMfaRecoveryAudits.AnyAsync(entry =>
                entry.InstanceId == request.InstanceId && entry.RequestReference == request.RequestReference,
                cancellationToken))
        {
            return false;
        }

        if (!(await users.SetTwoFactorEnabledAsync(user, false)).Succeeded ||
            !(await users.ResetAuthenticatorKeyAsync(user)).Succeeded)
        {
            return false;
        }
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        if (codes is null || codes.Any() || !(await users.UpdateSecurityStampAsync(user)).Succeeded)
        {
            return false;
        }

        db.OwnerMfaRecoveryAudits.Add(new OwnerMfaRecoveryAudit
        {
            Id = Guid.NewGuid(),
            OwnerId = user.Id,
            InstanceId = request.InstanceId,
            OperatorReference = request.OperatorReference,
            RequestReference = request.RequestReference,
            OccurredAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    [GeneratedRegex("\\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();
}
