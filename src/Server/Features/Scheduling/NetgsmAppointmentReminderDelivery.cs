using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Server.Features.Scheduling;

public sealed class NetgsmAppointmentReminderDelivery : IAppointmentReminderDelivery
{
    private static readonly Uri Endpoint = new("https://api.netgsm.com.tr/sms/rest/v2/send");
    private readonly HttpClient client;
    private readonly TimeProvider clock;
    private readonly string username;
    private readonly string password;
    private readonly string header;
    public bool Available { get; }
    public decimal MaximumUnitPriceTry { get; }

    public NetgsmAppointmentReminderDelivery(HttpClient client, IConfiguration config, TimeProvider clock)
    {
        this.client = client;
        this.clock = clock;
        Available = config.GetValue<bool>("Netgsm:RemindersEnabled");
        username = config["Netgsm:Username"] ?? "";
        password = config["Netgsm:Password"] ?? "";
        header = config["Netgsm:Header"] ?? "";
        if (!Available) return;
        MaximumUnitPriceTry = config.GetValue<decimal>("Netgsm:ReminderMaximumUnitPriceTry");
        if (username.Length is < 1 or > 64 || username.Any(c => c is < '0' or > '9') ||
            password.Length is < 1 or > 256 || password.Any(char.IsControl) ||
            header.Length is < 3 or > 11 || string.IsNullOrWhiteSpace(header) || header.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == ' ')) ||
            MaximumUnitPriceTry is <= 0 or > 0.50m || decimal.Round(MaximumUnitPriceTry, 2) != MaximumUnitPriceTry)
            throw new InvalidOperationException("Netgsm hatırlatma API alt kullanıcı, başlık ve vergi dahil azami segment bedeli yapılandırması geçersiz.");
    }

    public async Task<BookingSmsResult> SendAsync(string phone, DateTimeOffset startsAt, Guid deliveryId, CancellationToken token)
    {
        if (!Available) throw new InvalidOperationException("Randevu hatırlatma gönderimi kapalı.");
        var now = clock.GetUtcNow();
        // Sağlayıcı dakika hassasiyetindedir; bitişi ileri yuvarlayıp randevuyu aşma.
        var stop = startsAt.AddTicks(-(startsAt.Ticks % TimeSpan.TicksPerMinute));
        if (deliveryId == Guid.Empty || phone.Length != 13 || !phone.StartsWith("+905", StringComparison.Ordinal) ||
            phone.AsSpan(3).ContainsAnyExceptInRange('0', '9') || stop - now < TimeSpan.FromHours(1) || stop - now > TimeSpan.FromHours(21))
            return new(BookingSmsOutcome.PermanentFailure);
        var local = TimeZoneInfo.ConvertTime(stop, AppointmentRules.Istanbul);
        var message = "Randevu hatirlatma: " + local.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + " (Turkiye saati).";
        if (message.Length + 6 > 120 || !message.All(char.IsAscii)) return new(BookingSmsOutcome.PermanentFailure);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password)));
        request.Content = JsonContent.Create(new
        {
            msgheader = header,
            messages = new[] { new { msg = message, no = phone[3..] } },
            encoding = "UTF-8",
            iysfilter = "0",
            stopdate = local.ToString("ddMMyyyyHHmm", CultureInfo.InvariantCulture),
            referansID = deliveryId.ToString("N")
        });
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return new(BookingSmsOutcome.PermanentFailure);
            if (response.StatusCode != System.Net.HttpStatusCode.OK) return new(BookingSmsOutcome.Unknown);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = new byte[4097]; var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), timeout.Token);
                if (read == 0) break;
                count += read;
            }
            if (count > 4096) return new(BookingSmsOutcome.Unknown);
            using var json = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(x => x.Name).Any(x => x.Count() > 1) ||
                !root.TryGetProperty("code", out var status) || status.ValueKind != JsonValueKind.String)
                return new(BookingSmsOutcome.Unknown);
            var reference = root.TryGetProperty("jobid", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            if (status.GetString() == "00" && reference is { Length: >= 1 and <= 80 } && reference.All(c => c is >= '0' and <= '9'))
                return new(BookingSmsOutcome.Accepted, reference);
            if (root.TryGetProperty("jobid", out id) && id.ValueKind != JsonValueKind.Null) return new(BookingSmsOutcome.Unknown);
            return new(status.GetString() is "20" or "30" or "40" or "50" or "51" or "70"
                ? BookingSmsOutcome.PermanentFailure : BookingSmsOutcome.Unknown);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            // Kabul belirsizse kör tekrar yok; referansID teslim veya dedupe garantisi değildir.
            return new(BookingSmsOutcome.Unknown);
        }
    }
}
