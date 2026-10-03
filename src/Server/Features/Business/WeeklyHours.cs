using System.Globalization;

namespace Server.Features.Business;

// İşletme ve personel haftaları aynı yerel saat/gün doğrulamasını kullanır.
public static class WeeklyHours
{
    public sealed record DayRequest(int? Day, bool? IsClosed, string? OpensAt, string? ClosesAt);
    public sealed record DayResponse(int Day, bool IsClosed, string? OpensAt, string? ClosesAt);
    internal sealed record DayValue(int Day, bool IsClosed, int? Start, int? End);
    internal static string? Hour(int? minute) => minute is null ? null : new TimeOnly(minute.Value / 60, minute.Value % 60).ToString("HH:mm", CultureInfo.InvariantCulture);
    private static int? Minute(string? text) => text is { Length: 5 } &&
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value.Hour * 60 + value.Minute : null;
    internal static DayValue[] Validate(Guid version, DayRequest[]? input, out Dictionary<string, string[]> errors)
    {
        errors = [];
        if (version == Guid.Empty) errors["days"] = ["Güncel sürüm gerekli. Güncel saatleri yükleyin."];
        if (input is not { Length: 7 } || input.Any(item => item is null || item.Day is null or < 0 or > 6) || input.Select(item => item.Day).Distinct().Count() != 7)
        {
            errors["days"] = ["Haftanın yedi günü birer kez gönderilmeli."];
            return [];
        }
        var days = new List<DayValue>();
        foreach (var item in input)
        {
            var start = Minute(item.OpensAt); var end = Minute(item.ClosesAt);
            if (item.IsClosed is null || (item.IsClosed == true && (item.OpensAt is not null || item.ClosesAt is not null)))
                errors[$"day{item.Day}Closed"] = ["Gün durumu gerekli; çalışılmayan/kapalı günde saat gönderilmez."];
            if (item.IsClosed == false)
            {
                if (start is null) errors[$"day{item.Day}OpensAt"] = ["Başlangıcı SS:dd biçiminde girin (00:00–23:59)."];
                if (end is null) errors[$"day{item.Day}ClosesAt"] = ["Bitişi SS:dd biçiminde girin (00:00–23:59)."];
                else if (start is not null && end <= start) errors[$"day{item.Day}ClosesAt"] = ["Bitiş aynı gün içinde başlangıçtan sonra olmalı."];
            }
            days.Add(new DayValue(item.Day.GetValueOrDefault(), item.IsClosed == true, start, end));
        }
        return days.OrderBy(item => item.Day).ToArray();
    }
}
