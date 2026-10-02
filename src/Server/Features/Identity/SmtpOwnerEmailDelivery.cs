using Microsoft.EntityFrameworkCore;
using MimeKit;
using Server.Infrastructure;

namespace Server.Features.Identity;

public sealed class SmtpOwnerEmailDelivery(IdentityEmailOptions options, IServiceScopeFactory scopes,
    IIdentityEmailTransport transport, TimeProvider clock) : IOwnerEmailVerificationDelivery, IOwnerPasswordResetDelivery
{
    public bool Available => true;
    public bool CanDeliver(string email) => options.CanDeliver(email);

    public Task DeliverAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        SendAsync(email, token, expiresAt, "verify-owner-email", Guid.NewGuid(), cancellationToken);

    Task IOwnerPasswordResetDelivery.DeliverAsync(string email, string token, DateTimeOffset expiresAt,
        Guid deliveryId, CancellationToken cancellationToken) =>
        SendAsync(email, token, expiresAt, "reset-owner-password", deliveryId, cancellationToken);

    private async Task SendAsync(string email, string token, DateTimeOffset expiresAt, string purpose,
        Guid deliveryId, CancellationToken cancellationToken)
    {
        if (!CanDeliver(email) || expiresAt <= clock.GetUtcNow()) throw new IdentityEmailDeliveryException(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        // An atomic conditional upsert prevents concurrent sends from exceeding the installation's daily cap.
        var reserved = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "IdentityEmailQuotas" ("Day", "Attempts") VALUES ({day}, 1)
            ON CONFLICT ("Day") DO UPDATE SET "Attempts" = "IdentityEmailQuotas"."Attempts" + 1
            WHERE "IdentityEmailQuotas"."Attempts" < {options.DailyLimit}
            """, timeout.Token);
        if (reserved != 1) throw new IdentityEmailDeliveryException(false);
        var from = MailboxAddress.Parse(options.FromAddress);
        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Randevu", from.Address));
        message.To.Add(MailboxAddress.Parse(email));
        message.MessageId = $"{deliveryId:N}.{OwnerRecoveryEmailEndpoints.Hash(options.InstanceId)[..16]}@{from.Domain}";
        message.Subject = purpose == "verify-owner-email" ? "Hesap e-postanızı doğrulayın" : "Parolanızı yenileyin";
        var link = new Uri(options.PublicOrigin, "/#" + purpose + "=" + Uri.EscapeDataString(token));
        var description = purpose == "verify-owner-email" ? "Hesap e-postanızı doğrulamak için bağlantıyı açıp onaylayın:"
            : "Yeni parola belirlemek için bağlantıyı açın. Bu talebi siz yapmadıysanız iletiyi yok sayın:";
        var localExpiry = TimeZoneInfo.ConvertTime(expiresAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
        message.Body = new TextPart("plain")
        {
            Text = $"{description}\n{link.AbsoluteUri}\n\n" +
                $"Bağlantı 30 dakika geçerlidir. Son kullanım: {localExpiry:dd.MM.yyyy HH:mm} (Türkiye saati).\n" +
                "Bağlantıyı açmak parolanızı değiştirmez. İki aşamalı girişiniz korunur."
        };
        await transport.SendAsync(message, timeout.Token);
    }
}
