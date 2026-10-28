using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Server.Features.Scheduling;

internal static partial class AppointmentExcel
{
    internal const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal sealed record Row(DateTime StartsAt, DateTime EndsAt, string CustomerName, string CustomerPhone,
        string ServiceName, string StaffName, string Status);

    internal static byte[] Create(Row[] rows)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Add(zip, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Randevular" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Add(zip, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Add(zip, "xl/styles.xml", """
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <numFmts count="3"><numFmt numFmtId="164" formatCode="dd.mm.yyyy"/><numFmt numFmtId="165" formatCode="hh:mm"/><numFmt numFmtId="166" formatCode="dd.mm.yyyy hh:mm"/></numFmts>
                  <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="5">
                    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                    <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                    <xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                    <xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                    <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
                  </cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                </styleSheet>
                """);
            using var stream = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest).Open();
            using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
            xml.WriteStartElement("worksheet", Spreadsheet);
            xml.WriteStartElement("sheetViews"); xml.WriteStartElement("sheetView"); xml.WriteAttributeString("workbookViewId", "0");
            xml.WriteStartElement("pane"); xml.WriteAttributeString("ySplit", "1"); xml.WriteAttributeString("topLeftCell", "A2");
            xml.WriteAttributeString("activePane", "bottomLeft"); xml.WriteAttributeString("state", "frozen");
            xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement();
            xml.WriteStartElement("cols");
            int[] widths = [15, 15, 24, 30, 22, 40, 30, 22];
            for (var column = 0; column < widths.Length; column++)
            {
                xml.WriteStartElement("col"); xml.WriteAttributeString("min", (column + 1).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("max", (column + 1).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("width", widths[column].ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("customWidth", "1"); xml.WriteEndElement();
            }
            xml.WriteEndElement(); xml.WriteStartElement("sheetData");
            xml.WriteStartElement("row"); xml.WriteAttributeString("r", "1");
            string[] headers = ["Tarih", "Başlangıç", "Bitiş (İstanbul)", "Müşteri", "Telefon", "Hizmet", "Çalışan", "Durum"];
            for (var column = 0; column < headers.Length; column++) Text(xml, column, 1, headers[column], 4);
            xml.WriteEndElement();
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index]; var number = index + 2;
                var start = TimeZoneInfo.ConvertTimeFromUtc(row.StartsAt, AppointmentRules.Istanbul);
                var end = TimeZoneInfo.ConvertTimeFromUtc(row.EndsAt, AppointmentRules.Istanbul);
                xml.WriteStartElement("row"); xml.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
                Date(xml, 0, number, start.Date, 1); Date(xml, 1, number, start, 2); Date(xml, 2, number, end, 3);
                Text(xml, 3, number, row.CustomerName); Text(xml, 4, number, row.CustomerPhone);
                Text(xml, 5, number, row.ServiceName); Text(xml, 6, number, row.StaffName);
                Text(xml, 7, number, row.Status switch
                {
                    "Pending" => "Onay bekliyor",
                    "Confirmed" => "Onaylı",
                    "Cancelled" => "İptal edildi",
                    "Completed" => "Tamamlandı",
                    "NoShow" => "Gelmedi",
                    _ => throw new InvalidOperationException("Desteklenmeyen randevu durumu.")
                });
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); xml.WriteStartElement("autoFilter");
            xml.WriteAttributeString("ref", $"A1:H{rows.Length + 1}"); xml.WriteEndElement(); xml.WriteEndElement();
        }
        return output.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void Cell(XmlWriter xml, int column, int row, int style)
    {
        xml.WriteStartElement("c"); xml.WriteAttributeString("r", ((char)('A' + column)).ToString() + row.ToString(CultureInfo.InvariantCulture));
        if (style != 0) xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
    }

    private static void Text(XmlWriter xml, int column, int row, string value, int style = 0)
    {
        Cell(xml, column, row, style); xml.WriteAttributeString("t", "inlineStr"); xml.WriteStartElement("is"); xml.WriteStartElement("t");
        xml.WriteAttributeString("xml", "space", null, "preserve");
        // Kullanıcı metni formül değildir; OOXML kaçışına benzeyen gerçek metni de koru.
        value = EscapeSequence().Replace(value, match => "_x005F_" + match.Value[1..]);
        foreach (var rune in value.EnumerateRunes())
            if (rune.Value is 9 or 10 or 13 or >= 0x20 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF)
                xml.WriteString(rune.ToString());
        xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement();
    }

    private static void Date(XmlWriter xml, int column, int row, DateTime value, int style)
    {
        // Excel'in 1900 öncesi tarih gösterimi yok; desteklenen eski kayıt metin olarak kaybolmadan kalır.
        if (value.Year < 1900) { Text(xml, column, row, value.ToString(style == 1 ? "dd.MM.yyyy" : style == 2 ? "HH:mm" : "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)); return; }
        Cell(xml, column, row, style);
        var serial = value.ToOADate();
        if (value < new DateTime(1900, 3, 1)) serial--; // Excel'in gerçek olmayan 29.02.1900 günü.
        xml.WriteElementString("v", serial.ToString("R", CultureInfo.InvariantCulture)); xml.WriteEndElement();
    }

    [GeneratedRegex("_x[0-9a-fA-F]{4}_", RegexOptions.CultureInvariant)]
    private static partial Regex EscapeSequence();
}
