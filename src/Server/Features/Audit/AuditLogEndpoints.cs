using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace Server.Features.Audit;

public static class AuditLogEndpoints
{
    public sealed record Entry(string Id, DateTimeOffset OccurredAt, string Module, string Action, string Actor, string Target);
    public sealed record Page(Entry[] Items, string Category, string TimeZone, string Cursor, string? NextCursor);
    private sealed record Cursor(string Category, DateTimeOffset AsOf, DateTimeOffset? BeforeAt, int? Source, Guid? Id);

    public static void MapAuditLogEndpoints(this IEndpointRouteBuilder app) => app.MapGet("/api/audit-log/", ReadAsync)
        .RequireAuthorization("Owner").RequireRateLimiting("audit-log");

    private static bool TryCursor(string? value, string category, out Cursor? cursor)
    {
        cursor = null;
        if (value is null) return true;
        if (value.Length is < 1 or > 512) return false;
        try { cursor = JsonSerializer.Deserialize<Cursor>(WebEncoders.Base64UrlDecode(value)); }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException) { return false; }
        return cursor is not null && cursor.Category == category && cursor.AsOf.Offset == TimeSpan.Zero && cursor.AsOf >= DateTimeOffset.UnixEpoch &&
            ((cursor.BeforeAt is null && cursor.Source is null && cursor.Id is null) ||
             (cursor.BeforeAt is not null && cursor.BeforeAt.Value.Offset == TimeSpan.Zero && cursor.BeforeAt >= DateTimeOffset.UnixEpoch &&
              cursor.BeforeAt <= cursor.AsOf && cursor.Source is >= 1 and <= 13 && cursor.Id is not null && cursor.Id != Guid.Empty));
    }

    private static async Task<IResult> ReadAsync(HttpContext context, AppDbContext db, TimeProvider clock,
        string category = "all", string? cursor = null, int pageSize = 20)
    {
        context.Response.Headers.CacheControl = "no-store";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        if (category is not ("all" or "definitions" or "security"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["category"] = ["Geçerli bir kayıt kategorisi seçin."] });
        if (pageSize is < 1 or > 50)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["pageSize"] = ["Sayfa boyutu 1–50 arasında olmalı."] });
        if (!TryCursor(cursor, category, out var before))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["Sayfa bilgisi geçersiz. Listeyi yenileyin."] });
        var asOf = before?.AsOf ?? clock.GetUtcNow().ToUniversalTime();
        var rows = await db.Database.SqlQueryRaw<AuditLogQuery.Row>(AuditLogQuery.Sql,
            new NpgsqlParameter("category", category), new NpgsqlParameter("asOf", asOf),
            new NpgsqlParameter("hasCursor", before?.Id is not null), new NpgsqlParameter("beforeAt", before?.BeforeAt ?? asOf),
            new NpgsqlParameter("source", before?.Source ?? 13), new NpgsqlParameter("id", before?.Id ?? Guid.Empty),
            new NpgsqlParameter("limit", pageSize + 1)).ToArrayAsync(timeout.Token);
        var items = rows.Take(pageSize).Select(row => new Entry($"{row.Source}:{row.Id:D}", row.OccurredAt,
            Module(row.Source), Action(row.Source, row.Kind), row.Actor, row.Target)).ToArray();
        string? next = null;
        if (rows.Length > pageSize)
        {
            var last = rows[pageSize - 1];
            next = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Cursor(category, asOf, last.OccurredAt, last.Source, last.Id)));
        }
        var current = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(before ?? new Cursor(category, asOf, null, null, null)));
        return Results.Ok(new Page(items, category, "Europe/Istanbul", current, next));
    }

    private static string Module(int source) => source switch
    {
        1 => "İşletme bilgileri",
        2 => "İşletme logosu",
        3 => "İşletme saatleri",
        4 => "Personel",
        5 => "Hizmetler",
        6 => "Personel hizmetleri",
        7 => "Personel saatleri",
        8 => "Çalışan daveti",
        9 => "Çalışan parola sıfırlama",
        10 => "Çalışan erişimi",
        11 => "Owner parola kurtarma",
        12 => "Owner MFA kurtarma",
        13 => "Çalışan erişimi",
        _ => throw new InvalidOperationException("Bilinmeyen kayıt kaynağı.")
    };
    private static string Action(int source, string kind) => (source, kind) switch
    {
        (1 or 2 or 3 or 7, _) => "Güncellendi",
        (4 or 5, "Created") => "Eklendi",
        (4, "Renamed") => "Ad değiştirildi",
        (5, "Updated") => "Düzenlendi",
        (4 or 5, "Activated") => "Etkinleştirildi",
        (4 or 5, "Deactivated") => "Pasifleştirildi",
        (4 or 5, "Deleted") => "Silindi",
        (6, "Assigned") => "Hizmet eşleştirildi",
        (6, "Unassigned") => "Hizmet eşleştirmesi kaldırıldı",
        (8, "Issued") => "Davet oluşturuldu",
        (8, "Accepted") => "Davet kabul edildi",
        (8, "Revoked") => "Davet iptal edildi",
        (9 or 11, "Issued") => "Sıfırlama oluşturuldu",
        (11, "SelfIssued") => "Sıfırlama bağlantısı istendi",
        (9 or 11, "Completed") => "Parola sıfırlandı",
        (10, _) => "Hesap pasifleştirildi",
        (12, _) => "MFA kurtarıldı",
        (13, _) => "Hesap etkinleştirildi",
        _ => "İşlem kaydı"
    };
}
