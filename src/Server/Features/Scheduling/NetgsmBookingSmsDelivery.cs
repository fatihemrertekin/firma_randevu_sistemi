using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Server.Features.Scheduling;

public sealed class NetgsmBookingSmsDelivery : IBookingSmsDelivery
{
    private static readonly Uri Endpoint = new("https://api.netgsm.com.tr/sms/rest/v2/otp");
    private readonly HttpClient client;
    private readonly string username;
    private readonly string password;
    private readonly string header;
    public bool Available { get; }
    public decimal MaximumUnitPriceTry { get; }

    public NetgsmBookingSmsDelivery(HttpClient client, IConfiguration config)
    {
        this.client = client;
        Available = config.GetValue<bool>("Netgsm:Enabled");
        username = config["Netgsm:Username"] ?? "";
        password = config["Netgsm:Password"] ?? "";
        header = config["Netgsm:Header"] ?? "";
        if (!Available) return;
        MaximumUnitPriceTry = config.GetValue<decimal>("Netgsm:MaximumUnitPriceTry");
        if (username.Length is < 1 or > 64 || username.Any(c => c is < '0' or > '9') ||
            password.Length is < 1 or > 256 || password.Any(char.IsControl) ||
            header.Length is < 3 or > 11 || string.IsNullOrWhiteSpace(header) || header.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == ' ')) ||
            MaximumUnitPriceTry is <= 0 or > 0.50m || decimal.Round(MaximumUnitPriceTry, 2) != MaximumUnitPriceTry)
            throw new InvalidOperationException("Netgsm API alt kullanıcı, başlık ve vergi dahil azami segment bedeli yapılandırması geçersiz.");
    }

    public async Task<BookingSmsResult> SendAsync(string phone, string code, Guid deliveryId, CancellationToken token)
    {
        if (!Available) throw new InvalidOperationException("SMS gönderimi kapalı.");
        if (deliveryId == Guid.Empty || phone.Length != 13 || !phone.StartsWith("+905", StringComparison.Ordinal) ||
            phone.AsSpan(3).ContainsAnyExceptInRange('0', '9') || code.Length != 6 || code.Any(c => c is < '0' or > '9'))
            return new(BookingSmsOutcome.PermanentFailure);
        var message = $"Randevu dogrulama kodunuz: {code}. Kodu kimseyle paylasmayin.";
        // Sabit ASCII metin, sağlayıcının eklediği operatör koduyla da tek segment içinde kalır.
        if (message.Length + 6 > 155) return new(BookingSmsOutcome.PermanentFailure);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password)));
        request.Content = JsonContent.Create(new { msgheader = header, msg = message, no = phone[3..] });
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
            return new(status.GetString() is "20" or "30" or "40" or "41" or "50" or "52" or "60" or "70"
                ? BookingSmsOutcome.PermanentFailure : BookingSmsOutcome.Unknown);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            // Yanıt bilinmiyorsa kabul edilmediği varsayılmaz; sır/gövde/telefon loglanmaz.
            return new(BookingSmsOutcome.Unknown);
        }
    }
}
