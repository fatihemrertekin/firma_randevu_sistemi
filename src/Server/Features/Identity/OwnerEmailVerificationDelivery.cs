using System.Net.Mail;
using System.Text;

namespace Server.Features.Identity;

public interface IOwnerEmailVerificationDelivery
{
    bool CanDeliver(string email);
    Task DeliverAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}

// A local development mailbox, never an Internet sender. No production fallback.
public sealed class LocalOwnerEmailVerificationDelivery : IOwnerEmailVerificationDelivery, IOwnerPasswordResetDelivery
{
    private readonly string? directory;
    private readonly Uri? origin;

    public LocalOwnerEmailVerificationDelivery(IConfiguration configuration, IWebHostEnvironment environment)
    {
        directory = configuration["RecoveryEmail:LocalPickupDirectory"];
        if (string.IsNullOrWhiteSpace(directory)) { directory = null; return; }
        if (!environment.IsDevelopment() || !OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("Yerel e-posta teslimi yalnız Linux geliştirme ortamında, özel tam dizin yoluyla açılabilir.");
        if (!Uri.TryCreate(configuration["RecoveryEmail:PublicOrigin"], UriKind.Absolute, out var parsed) ||
            parsed.Scheme != "http" || !parsed.IsLoopback || parsed.AbsolutePath != "/" ||
            parsed.Query.Length != 0 || parsed.Fragment.Length != 0 || parsed.UserInfo.Length != 0)
            throw new InvalidOperationException("Yerel e-posta için açıkça tanımlanmış localhost adresi gerekli.");
        origin = parsed;
        var fullPath = Path.GetFullPath(directory);
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (fullPath == webRoot || fullPath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Yerel posta dizini web kökünün dışında olmalı.");
        directory = fullPath;
    }

    public bool CanDeliver(string email) => directory is not null &&
        !email.Any(char.IsControl) && MailAddress.TryCreate(email, out var address) &&
        address.Address == email && address.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase);

    public Task DeliverAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        WriteAsync(email, token, expiresAt, "verify-owner-email", Guid.NewGuid(), cancellationToken);

    Task IOwnerPasswordResetDelivery.DeliverAsync(string email, string token, DateTimeOffset expiresAt,
        Guid deliveryId, CancellationToken cancellationToken) =>
        WriteAsync(email, token, expiresAt, "reset-owner-password", deliveryId, cancellationToken);

    private async Task WriteAsync(string email, string token, DateTimeOffset expiresAt, string purpose,
        Guid deliveryId, CancellationToken cancellationToken)
    {
        if (!CanDeliver(email) || directory is null || origin is null || !OperatingSystem.IsLinux())
            throw new IOException("Yerel doğrulama teslimi kullanılamıyor.");
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var permissions = File.GetUnixFileMode(directory);
        if ((permissions & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0 ||
            new DirectoryInfo(directory).LinkTarget is not null)
            throw new IOException("Yerel posta dizini özel ve bağlantısız olmalı.");
        var link = new Uri(origin, "/#" + purpose + "=" + Uri.EscapeDataString(token)).AbsoluteUri;
        var description = purpose == "verify-owner-email" ? "Hesap e-postanızı doğrulamak için bağlantıyı açıp onaylayın:"
            : "Parolanızı yenilemek için bağlantıyı açın. İstemediyseniz bu iletiyi yok sayın:";
        var body = $"{description}\n{link}\n" +
            $"Son geçerlilik (UTC): {expiresAt:O}\nBağlantıyı açmak parolanızı değiştirmez. İki aşamalı girişiniz korunur.";
        var message = $"From: no-reply@example.test\r\nTo: {email}\r\n" +
            $"Subject: Randevu - {(purpose == "verify-owner-email" ? "E-posta dogrulama" : "Parola yenileme")}\r\nMIME-Version: 1.0\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(body), Base64FormattingOptions.InsertLineBreaks);
        var filePath = Path.Combine(directory, deliveryId.ToString("N") + ".eml");
        if (File.Exists(filePath)) return; // Stable delivery ID prevents duplicates after an acknowledgement/restart failure.
        var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            }))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(message), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            try { File.Move(temporaryPath, filePath); }
            catch (IOException) when (File.Exists(filePath)) { /* Another valid lease delivered the same grant. */ }
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
