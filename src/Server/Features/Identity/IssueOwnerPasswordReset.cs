using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class IssueOwnerPasswordReset
{
    internal const string EnvelopePurpose = "OwnerPasswordReset.Grant.v1";
    public sealed record IssueRequest(string InstanceId, Guid OwnerId, string OperatorReference, string RequestReference);
    public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

    public static async Task<IssuedToken?> IssueAsync(
        IServiceProvider services, IssueRequest request, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (string.IsNullOrWhiteSpace(request.InstanceId) || request.InstanceId.Length > 128 ||
            request.InstanceId != configuration["Auth:InstanceId"] || request.OwnerId == Guid.Empty ||
            !ValidReference(request.OperatorReference) || !ValidReference(request.RequestReference)) return null;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {request.OwnerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || !user.EmailConfirmed || !await users.IsInRoleAsync(user, "Owner") ||
            await db.OwnerPasswordResetAudits.AnyAsync(entry => entry.InstanceId == request.InstanceId &&
                entry.RequestReference == request.RequestReference, cancellationToken)) return null;

        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var grantId = Guid.NewGuid();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        // Bind the immutable issuance record to the Identity token without storing the token in the DB.
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(EnvelopePurpose);
        var envelope = protector.Protect($"{grantId:N}\n{token}");
        db.OwnerPasswordResetAudits.Add(new OwnerPasswordResetAudit
        {
            Id = Guid.NewGuid(),
            GrantId = grantId,
            OwnerId = user.Id,
            InstanceId = request.InstanceId,
            OperatorReference = request.OperatorReference,
            RequestReference = request.RequestReference,
            Kind = "Issued",
            OccurredAt = now,
            ExpiresAt = now.AddMinutes(30)
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new IssuedToken(envelope, now.AddMinutes(30));
    }

    internal static bool ValidReference(string? value) => value is { Length: >= 1 and <= 64 } &&
        char.IsAsciiLetterOrDigit(value[0]) && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var instance = Environment.GetEnvironmentVariable("APP_RESET_INSTANCE_ID") ?? "";
        var ownerInput = Environment.GetEnvironmentVariable("APP_RESET_OWNER_ID");
        var output = Environment.GetEnvironmentVariable("APP_RESET_OUTPUT_PATH");
        if (!Guid.TryParse(ownerInput, out var ownerId) || ownerId == Guid.Empty ||
            Environment.GetEnvironmentVariable("APP_RESET_CONFIRM") != $"{instance}/{ownerId}" ||
            string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output))
        {
            Console.Error.WriteLine("Doğrulanmış firma/Owner onayı ve tam çıktı dosyası yolu gerekli.");
            return 1;
        }

        // The operator prepares a private directory first; never overwrite or print a token.
        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var file = new FileStream(output, fileOptions);
        var issued = await IssueAsync(services, new IssueRequest(instance, ownerId,
            Environment.GetEnvironmentVariable("APP_RESET_OPERATOR_REF") ?? "",
            Environment.GetEnvironmentVariable("APP_RESET_REQUEST_REF") ?? ""), timeout.Token);
        if (issued is null)
        {
            Console.Error.WriteLine("Token üretimi reddedildi; hedefi ve işlem referansını kontrol edin.");
            return 1;
        }
        await file.WriteAsync(Encoding.UTF8.GetBytes(issued.Token), timeout.Token);
        await file.FlushAsync(timeout.Token);
        Console.WriteLine("Token özel dosyaya yazıldı; 30 dakika içinde doğrulanmış özel kanaldan teslim edin.");
        return 0;
    }
}
