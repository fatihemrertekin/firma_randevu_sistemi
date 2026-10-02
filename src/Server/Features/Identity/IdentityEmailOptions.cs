using MimeKit;

namespace Server.Features.Identity;

public sealed class IdentityEmailOptions
{
    public string Host { get; }
    public int Port { get; }
    public string Username { get; }
    public string Password { get; }
    public string FromAddress { get; }
    public Uri PublicOrigin { get; }
    public int DailyLimit { get; }
    public string InstanceId { get; }
    private readonly string[] recipients;

    public IdentityEmailOptions(IConfiguration configuration, IWebHostEnvironment environment)
    {
        Host = configuration["IdentityEmail:Smtp:Host"] ?? "";
        Port = configuration.GetValue<int>("IdentityEmail:Smtp:Port", 587);
        Username = configuration["IdentityEmail:Smtp:Username"] ?? "";
        Password = configuration["IdentityEmail:Smtp:Password"] ?? "";
        FromAddress = configuration["IdentityEmail:Smtp:FromAddress"] ?? "";
        DailyLimit = configuration.GetValue<int>("IdentityEmail:Smtp:DailyLimit", 50);
        InstanceId = configuration["Auth:InstanceId"] ?? "";
        recipients = configuration.GetSection("IdentityEmail:Smtp:AllowedRecipients").Get<string[]>() ?? [];
        if (string.IsNullOrWhiteSpace(Host) || Host.Length > 253 ||
            Host.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-') ||
            Host.StartsWith('-') || Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(Username) || Username.Length > 254 || Username.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(Password) || Password.Length > 2048 || Password.Any(char.IsControl) ||
            !IsAddress(FromAddress) || DailyLimit is < 1 or > 500 ||
            string.IsNullOrWhiteSpace(InstanceId) || InstanceId.Length > 128 || recipients.Any(email => !IsAddress(email)))
            throw new InvalidOperationException("SMTP ayarları eksik veya geçersiz. Güvenli port, gönderen ve günlük sınırı kontrol edin.");
        if (!string.IsNullOrWhiteSpace(configuration["RecoveryEmail:LocalPickupDirectory"]))
            throw new InvalidOperationException("SMTP ve yerel dosya teslimi birlikte açılamaz.");
        if (!Uri.TryCreate(configuration["IdentityEmail:PublicOrigin"], UriKind.Absolute, out var origin) ||
            origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 ||
            (origin.Scheme != "https" && !(environment.IsDevelopment() && origin.Scheme == "http" && origin.IsLoopback)))
            throw new InvalidOperationException("E-posta bağlantıları için sabit HTTPS adresi veya geliştirmede localhost gerekli.");
        if (environment.IsDevelopment() && recipients.Length == 0)
            throw new InvalidOperationException("Geliştirmede SMTP yalnız açıkça seçilmiş test alıcılarına gönderilebilir.");
        if (!environment.IsDevelopment() && origin.IsLoopback)
            throw new InvalidOperationException("Üretimde localhost bağlantısı kullanılamaz.");
        PublicOrigin = origin;
    }

    public static bool IsAddress(string email) => email.Length is > 0 and <= 254 && !email.Any(char.IsControl) &&
        !email.Any(char.IsWhiteSpace) && MailboxAddress.TryParse(email, out var address) && address.Address == email &&
        address.Domain.Contains('.', StringComparison.Ordinal);

    public bool CanDeliver(string email) => IsAddress(email) && (recipients.Length == 0 ||
        recipients.Contains(email, StringComparer.OrdinalIgnoreCase));
}
