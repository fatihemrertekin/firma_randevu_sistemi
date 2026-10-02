using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Server.Features.Identity;

public sealed class IdentityEmailDeliveryException(bool retryable) : IOException("E-posta gönderimi tamamlanamadı.")
{
    public bool Retryable { get; } = retryable;
}

// The boundary is intentionally one operation; reset rules do not depend on MailKit.
public interface IIdentityEmailTransport
{
    Task SendAsync(MimeMessage message, CancellationToken cancellationToken);
}

public sealed class SmtpIdentityEmailTransport(IdentityEmailOptions options) : IIdentityEmailTransport
{
    public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = 10000 };
        // Default certificate validation remains enabled. STARTTLS must succeed; no plaintext fallback.
        try
        {
            await client.ConnectAsync(options.Host, options.Port,
                options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
        }
        catch (SmtpCommandException failure) { throw new IdentityEmailDeliveryException((int)failure.StatusCode < 500); }
        catch (Exception failure) when (failure is AuthenticationException or SslHandshakeException or
            System.Security.Authentication.AuthenticationException or NotSupportedException)
        { throw new IdentityEmailDeliveryException(false); }
        catch (Exception failure) when (failure is IOException or SocketException or SmtpProtocolException)
        { throw new IdentityEmailDeliveryException(true); }
        // SMTP accepted the message. A failed QUIT must not cause an unnecessary resend.
        try { await client.DisconnectAsync(true, cancellationToken); }
        catch (Exception failure) when (failure is IOException or SocketException or SmtpProtocolException or OperationCanceledException) { }
    }
}
