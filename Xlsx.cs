using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RedZoneClub;

/// <summary>كاتب ملفات Excel (xlsx) بدون أي مكتبات خارجية.</summary>
public class XCell
{
    public object? V; public int S;
    public XCell(object? v, int s) { V = v; S = s; }
}

public class XSheet
{
    public string Name; public double[] Widths; public bool Freeze;
    public List<XCell?[]> Rows = new();
    public XSheet(string name, double[] widths, bool freeze = false) { Name = name; Widths = widths; Freeze = freeze; }
    public void Add(params XCell?[] cells) => Rows.Add(cells);
}

public static class X
{
    // أنماط الخلايا
    public const int Plain = 0, Head = 1, Txt = 2, Num = 3, Title = 4, TotL = 5, TotN = 6;
    public static XCell T(string s, int st = Txt) => new(s, st);
    public static XCell N(double d, int st = Num) => new(d, st);

    const string Styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"4\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"16\"/><color rgb=\"FFE5192D\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE5192D\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF3F4F6\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=\"thin\"><color rgb=\"FFD1D5DB\"/></left><right style=\"thin\"><color rgb=\"FFD1D5DB\"/></right><top style=\"thin\"><color rgb=\"FFD1D5DB\"/></top><bottom style=\"thin\"><color rgb=\"FFD1D5DB\"/></bottom><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"7\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"3\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/><xf numFmtId=\"0\" fontId=\"3\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf numFmtId=\"3\" fontId=\"3\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";

    static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            if (ch < 32 && ch != '\t' && ch != '\n' && ch != '\r') continue;
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    static string Col(int n)
    {
        var s = ""; n++;
        while (n > 0) { int r = (n - 1) % 26; s = (char)(65 + r) + s; n = (n - 1) / 26; }
        return s;
    }

    static string SheetName(string n)
    {
        foreach (var c in new[] { '[', ']', ':', '*', '?', '/', '\\' }) n = n.Replace(c, ' ');
        n = n.Trim(); if (n.Length == 0) n = "Sheet"; if (n.Length > 31) n = n[..31];
        return n;
    }

    static string SheetXml(XSheet sh)
    {
        var o = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\" rightToLeft=\"1\"");
        if (sh.Freeze) o.Append("><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        else o.Append("/></sheetViews>");
        o.Append("<sheetFormatPr defaultRowHeight=\"15\"/><cols>");
        for (int i = 0; i < sh.Widths.Length; i++)
            o.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{sh.Widths[i].ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        o.Append("</cols><sheetData>");
        for (int r = 0; r < sh.Rows.Count; r++)
        {
            o.Append($"<row r=\"{r + 1}\">");
            var cells = sh.Rows[r];
            for (int c = 0; c < cells.Length; c++)
            {
                var cell = cells[c]; if (cell == null) continue;
                string rf = Col(c) + (r + 1);
                if (cell.V is null || (cell.V is string e0 && e0.Length == 0)) o.Append($"<c r=\"{rf}\" s=\"{cell.S}\"/>");
                else if (cell.V is string str) o.Append($"<c r=\"{rf}\" s=\"{cell.S}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(str)}</t></is></c>");
                else
                {
                    double d = Convert.ToDouble(cell.V, CultureInfo.InvariantCulture);
                    if (double.IsNaN(d) || double.IsInfinity(d)) d = 0;
                    o.Append($"<c r=\"{rf}\" s=\"{cell.S}\"><v>{d.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                }
            }
            o.Append("</row>");
        }
        o.Append("</sheetData></worksheet>");
        return o.ToString();
    }

    static void Put(ZipArchive z, string name, string content)
    {
        var e = z.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        var b = new UTF8Encoding(false).GetBytes(content);
        s.Write(b, 0, b.Length);
    }

    public static byte[] Build(List<XSheet> sheets)
    {
        int n = sheets.Count;
        var ct = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 0; i < n; i++) ct.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        ct.Append("</Types>");
        const string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";
        var wb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        var wr = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < n; i++)
        {
            wb.Append($"<sheet name=\"{Esc(SheetName(sheets[i].Name))}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            wr.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        }
        wb.Append("</sheets></workbook>");
        wr.Append($"<Relationship Id=\"rId{n + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Put(z, "[Content_Types].xml", ct.ToString());
            Put(z, "_rels/.rels", rels);
            Put(z, "xl/workbook.xml", wb.ToString());
            Put(z, "xl/_rels/workbook.xml.rels", wr.ToString());
            Put(z, "xl/styles.xml", Styles);
            for (int i = 0; i < n; i++) Put(z, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
        }
        return ms.ToArray();
    }
}
