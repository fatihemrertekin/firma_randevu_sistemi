using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class AppointmentExportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Endpoint = "/api/appointments/export?from=2026-10-29&to=2026-10-29";
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static Guid Add(AppDbContext db, Guid owner, Guid member, Guid service, DateTime start,
        string name = "Sentetik müşteri", string status = "Cancelled", Guid? id = null)
    {
        var key = id ?? Guid.NewGuid();
        db.Appointments.Add(new Appointment
        {
            Id = key,
            ActorId = owner,
            RequestKey = Guid.NewGuid(),
            RequestHash = new string('A', 64),
            CustomerName = name,
            CustomerPhone = "+905000000001",
            CreatedAt = start.AddDays(-1),
            Version = Guid.NewGuid()
        });
        db.AppointmentTimeBlocks.Add(new AppointmentTimeBlock
        {
            AppointmentId = key,
            StaffMemberId = member,
            ServiceDefinitionId = service,
            StaffName = "Eski çalışan adı",
            ServiceName = "Hizmet <&> \";\nÖrnek",
            DurationMinutes = 1,
            Price = 123.45m,
            Currency = "TRY",
            StartsAt = start,
            EndsAt = start.AddMinutes(1),
            OccupiedStartsAt = start,
            OccupiedEndsAt = start.AddMinutes(1),
            Status = status,
            PendingExpiresAt = status == "Pending" ? start.AddMinutes(-1) : null
        });
        return key;
    }

    private static XDocument Read(ZipArchive zip, string path)
    {
        using var stream = Assert.IsType<ZipArchiveEntry>(zip.GetEntry(path)).Open();
        return XDocument.Load(stream);
    }

    private static async Task<Dictionary<string, string>> Snapshot(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync(Token);
        var connection = db.Database.GetDbConnection(); var names = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename";
            await using var reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token)) names.Add(reader.GetString(0));
        }
        var result = new Dictionary<string, string>();
        foreach (var name in names)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT md5(coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text,'')) FROM \"" + name.Replace("\"", "\"\"") + "\" t";
            result[name] = Assert.IsType<string>(await command.ExecuteScalarAsync(Token));
        }
        return result;
    }

    [Fact]
    public async Task WorkbookUsesIstanbulRangeStableHistoricalRowsAndLiteralCellsWithoutWrites()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var members = await db.StaffMembers.OrderBy(x => x.Id).ToArrayAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        var start = new DateTime(2026, 10, 28, 21, 0, 0, DateTimeKind.Utc);
        var names = new[] { "=HYPERLINK(\"https://example.invalid\")", "+Örnek", "-Örnek", "@Örnek", "İpek 😀 _x0041_", "  Çınar,\"Örnek\"\nİkinci satır  " };
        var statuses = new[] { "Confirmed", "Cancelled", "Pending", "Completed", "NoShow", "Cancelled" };
        Add(db, seed.OwnerId, members[0].Id, service.Id, start.AddMinutes(-1), "Dışarıdaki önceki kayıt");
        for (var index = 0; index < names.Length; index++)
            Add(db, seed.OwnerId, members[0].Id, service.Id, index == 5 ? start.AddMinutes(1439) : start.AddMinutes(index < 2 ? 0 : index), names[index], statuses[index],
                Guid.Parse($"00000045-0000-0000-0000-{index + 1:D12}"));
        Add(db, seed.OwnerId, members[0].Id, service.Id, start.AddDays(1), "Dışarıdaki sonraki kayıt");
        Add(db, seed.OwnerId, members[1].Id, service.Id, start.AddHours(2), "Başka çalışan");
        members[0].IsActive = false; members[0].IsDeleted = true; members[0].Name = "Yeni çalışan adı";
        service.IsActive = false; service.IsDeleted = true; service.Name = "Yeni hizmet adı";
        await db.SaveChangesAsync(Token); var before = await Snapshot(db);

        using var response = await owner.GetAsync(Endpoint + $"&staffMemberId={members[0].Id}", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("randevular_2026-10-29_2026-10-29.xlsx", response.Content.Headers.ContentDisposition?.ToString());
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync(Token)); using var zip = new ZipArchive(bytes);
        Assert.Equal(6, zip.Entries.Count); Assert.All(zip.Entries, entry => Assert.True(entry.Length < 4_000_000));
        foreach (var entry in zip.Entries)
        { using var stream = entry.Open(); Assert.DoesNotContain("TargetMode=\"External\"", new StreamReader(stream).ReadToEnd()); }
        var workbook = Read(zip, "xl/workbook.xml"); Assert.Equal("Randevular", workbook.Descendants(Sheet + "sheet").Single().Attribute("name")?.Value);
        var sheet = Read(zip, "xl/worksheets/sheet1.xml"); var rows = sheet.Descendants(Sheet + "row").ToArray();
        Assert.Equal(7, rows.Length); Assert.All(rows, row => Assert.Equal(8, row.Elements(Sheet + "c").Count()));
        Assert.Empty(sheet.Descendants(Sheet + "f")); Assert.Empty(sheet.Descendants(Sheet + "hyperlink"));
        Assert.Equal("A2", sheet.Descendants(Sheet + "pane").Single().Attribute("topLeftCell")?.Value);
        Assert.Equal("A1:H7", sheet.Descendants(Sheet + "autoFilter").Single().Attribute("ref")?.Value);
        var headers = rows[0].Descendants(Sheet + "t").Select(x => x.Value).ToArray();
        Assert.Equal(["Tarih", "Başlangıç", "Bitiş (İstanbul)", "Müşteri", "Telefon", "Hizmet", "Çalışan", "Durum"], headers);
        var cells = sheet.Descendants(Sheet + "c").ToDictionary(x => x.Attribute("r")!.Value);
        Assert.Equal(names[0], cells["D2"].Descendants(Sheet + "t").Single().Value);
        Assert.Equal("İpek 😀 _x005F_x0041_", cells["D6"].Descendants(Sheet + "t").Single().Value);
        Assert.Equal(names[5], cells["D7"].Descendants(Sheet + "t").Single().Value);
        foreach (var row in rows.Skip(1))
        {
            Assert.All(row.Elements(Sheet + "c").Skip(3), cell => Assert.Equal("inlineStr", cell.Attribute("t")?.Value));
            Assert.Equal("+905000000001", row.Elements(Sheet + "c").ElementAt(4).Value);
            Assert.Equal("Eski çalışan adı", row.Elements(Sheet + "c").ElementAt(6).Value);
            Assert.Equal("Hizmet <&> \";\nÖrnek", row.Elements(Sheet + "c").ElementAt(5).Value);
        }
        Assert.Equal(new DateTime(2026, 10, 29), DateTime.FromOADate(double.Parse(cells["A2"].Value, CultureInfo.InvariantCulture)));
        Assert.Equal(new DateTime(2026, 10, 30), DateTime.FromOADate(double.Parse(cells["C7"].Value, CultureInfo.InvariantCulture)));
        Assert.Equal(["Onaylı", "İptal edildi", "Onay bekliyor", "Tamamlandı", "Gelmedi", "İptal edildi"], rows.Skip(1).Select(row => row.Elements(Sheet + "c").Last().Value));
        Assert.Equal(before.OrderBy(x => x.Key), (await Snapshot(db)).OrderBy(x => x.Key));
    }

    [Fact]
    public async Task MaximumRowsAreCompleteAndAnExtraRowRejectsInsteadOfTruncating()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        var start = new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < 5000; index++) Add(db, seed.OwnerId, member.Id, service.Id, start);
        await db.SaveChangesAsync(Token);
        using (var response = await owner.GetAsync(Endpoint, Token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync(Token)); using var zip = new ZipArchive(bytes);
            Assert.Equal(5001, Read(zip, "xl/worksheets/sheet1.xml").Descendants(Sheet + "row").Count());
        }
        Add(db, seed.OwnerId, member.Id, service.Id, start); await db.SaveChangesAsync(Token); var before = await Snapshot(db);
        using var rejected = await owner.GetAsync(Endpoint, Token); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.NotEqual(ContentType, rejected.Content.Headers.ContentType?.MediaType); Assert.Null(rejected.Content.Headers.ContentDisposition);
        Assert.Contains("5.000", await rejected.Content.ReadAsStringAsync(Token));
        Assert.Equal(before.OrderBy(x => x.Key), (await Snapshot(db)).OrderBy(x => x.Key));
    }

    [Fact]
    public async Task EmptyValidationOwnerMfaAndSharedLimitDoNotWrite()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var anonymous = app.CreateClient(); using var pending = app.CreateClient();
        await PasswordStepAsync(pending); await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        { using var response = await client.GetAsync(Endpoint, Token); Assert.Equal(status, response.StatusCode); Assert.NotEqual(ContentType, response.Content.Headers.ContentType?.MediaType); }
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var before = await Snapshot(db);
        foreach (var query in new[] { "", "from=2026-10-29", "from=2026-02-30&to=2026-03-01", "from=2026-10-30&to=2026-10-29",
            "from=2026-10-01&to=2026-11-01", "from=0001-01-01&to=0001-01-01", "from=9999-12-31&to=9999-12-31",
            "from=2026-10-29&to=2026-10-29&staffMemberId=wrong", "from=2026-10-29&to=2026-10-29&staffMemberId=00000000-0000-0000-0000-000000000000" })
        { using var response = await owner.GetAsync("/api/appointments/export?" + query, Token); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        using (var response = await owner.GetAsync("/api/appointments/export?from=2026-10-01&to=2026-10-31", Token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync(Token));
            using var zip = new ZipArchive(bytes); Assert.Single(Read(zip, "xl/worksheets/sheet1.xml").Descendants(Sheet + "row"));
        }
        var limited = false;
        for (var index = 0; index < 61; index++)
        {
            using var response = await owner.GetAsync(index % 3 == 0 ? Endpoint : index % 3 == 1 ? "/api/appointments/" : "/api/appointments/week", Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            { Assert.InRange(Assert.IsType<TimeSpan>(response.Headers.RetryAfter?.Delta).TotalSeconds, 1, 60); limited = true; break; }
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.True(limited); Assert.Equal(before.OrderBy(x => x.Key), (await Snapshot(db)).OrderBy(x => x.Key));
    }

    [Fact]
    public async Task EarlyDatesRemainReadableAndExcel1900SerialIsCorrect()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        Add(db, seed.OwnerId, member.Id, service.Id, new DateTime(1899, 12, 31, 10, 0, 0, DateTimeKind.Utc), "Eski tarih");
        Add(db, seed.OwnerId, member.Id, service.Id, new DateTime(1900, 1, 1, 10, 0, 0, DateTimeKind.Utc), "İlk Excel günü"); await db.SaveChangesAsync(Token);
        using var response = await owner.GetAsync("/api/appointments/export?from=1899-12-31&to=1900-01-01", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync(Token));
        using var zip = new ZipArchive(bytes); var cells = Read(zip, "xl/worksheets/sheet1.xml").Descendants(Sheet + "c").ToDictionary(x => x.Attribute("r")!.Value);
        Assert.Equal("inlineStr", cells["A2"].Attribute("t")?.Value); Assert.Equal("31.12.1899", cells["A2"].Value); Assert.Equal("1", cells["A3"].Value);
    }
}
