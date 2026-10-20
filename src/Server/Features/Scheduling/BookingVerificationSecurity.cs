using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public sealed class BookingVerificationSecurity
{
    public const string PayloadPurpose = "BookingVerification.Sms.v1";
    private readonly byte[] key = [];
    private readonly string instance;
    public bool Enabled { get; }
    public bool DispatchEnabled { get; }
    public Guid BudgetApprovalId { get; }
    internal string InstanceId => instance;
    private readonly IBookingSmsDelivery delivery;
    internal decimal MaximumUnitPriceTry => delivery.MaximumUnitPriceTry;

    public BookingVerificationSecurity(IConfiguration config, IBookingSmsDelivery delivery)
    {
        this.delivery = delivery;
        instance = config["Auth:InstanceId"] ?? "";
        Enabled = config.GetValue<bool>("BookingVerification:Enabled");
        if (!Enabled) return;
        if (!delivery.Available || string.IsNullOrWhiteSpace(instance))
            throw new InvalidOperationException("Telefon doğrulaması için kullanılabilir taşıyıcı ve firma kimliği gerekli.");
        try { key = Convert.FromBase64String(config["BookingVerification:HmacKey"] ?? ""); }
        catch (FormatException) { throw new InvalidOperationException("Telefon doğrulama anahtarı geçersiz."); }
        if (key.Length != 32) throw new InvalidOperationException("Telefon doğrulama anahtarı 32 bayt olmalı.");
        DispatchEnabled = config.GetValue<bool>("BookingSms:DispatchEnabled");
        if (DispatchEnabled)
        {
            if (!Guid.TryParse(config["BookingSms:BudgetApprovalId"], out var approval) || approval == Guid.Empty)
                throw new InvalidOperationException("SMS gönderimi için kalıcı bütçe onay kimliği gerekli.");
            BudgetApprovalId = approval;
        }
    }

    public string Hash(string purpose, params object[] values) => Convert.ToHexString(HMACSHA256.HashData(key,
        JsonSerializer.SerializeToUtf8Bytes(new { Instance = instance, Purpose = purpose, Values = values })));
    public static string SecretHash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    public static bool Equal(string? first, string second) => first is not null && first.Length == second.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(first), Encoding.ASCII.GetBytes(second));
    public static string Code() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    public sealed record Payload(string Phone, string Code);
    public static string Protect(IDataProtectionProvider provider, string phone, string code) =>
        provider.CreateProtector(PayloadPurpose).Protect(JsonSerializer.Serialize(new Payload(phone, code)));

    // Küçük pilotta tüm OTP yazımları kısa tek kilitte sıralanır; dış çağrı bu kilidi tutmaz.
    public string KeyId => Hash("key-binding");

    internal async Task LockAsync(AppDbContext db, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", token);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(710502)", token);
        if (await db.BookingVerificationQuotaEvents.AnyAsync(x => x.KeyId != KeyId, token))
            throw new InvalidOperationException("Telefon doğrulama anahtarı kayıtlı kota anahtarıyla uyuşmuyor.");
    }

    internal static DateTimeOffset DayStart(DateTimeOffset now) =>
        CalendarStart(TimeZoneInfo.ConvertTime(now, AppointmentRules.Istanbul).Date);
    internal static DateTimeOffset DayEnd(DateTimeOffset now, int days = 1) =>
        CalendarStart(TimeZoneInfo.ConvertTime(now, AppointmentRules.Istanbul).Date.AddDays(days));
    internal static DateTimeOffset CalendarStart(DateTime localDate) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), AppointmentRules.Istanbul));
}
