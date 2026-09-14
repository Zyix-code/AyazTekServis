using System.IO.Compression;
using System.Security;
using System.Text;

namespace AyazTekServis.Services;

public static class XlsxExportService
{
    public static byte[] Create(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Add(zip,"[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Add(zip,"_rels/.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(zip,"xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Add(zip,"xl/workbook.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"{Esc(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(zip,"xl/styles.xml", """<?xml version="1.0" encoding="UTF-8"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="1"><fill><patternFill patternType="none"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs></styleSheet>""");
            var sb=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            int r=1; sb.Append($"<row r=\"{r}\">"); for(int c=0;c<headers.Count;c++) Cell(sb,r,c,headers[c],1); sb.Append("</row>");
            foreach(var row in rows){r++;sb.Append($"<row r=\"{r}\">"); for(int c=0;c<row.Count;c++) Cell(sb,r,c,row[c],0); sb.Append("</row>");}
            sb.Append("</sheetData><autoFilter ref=\"A1:").Append(Col(headers.Count-1)).Append(r).Append("\"/></worksheet>");
            Add(zip,"xl/worksheets/sheet1.xml",sb.ToString());
        }
        return ms.ToArray();
    }
    private static void Cell(StringBuilder sb,int row,int col,object? value,int style){var s=value switch{DateTime d=>d.ToString("dd.MM.yyyy HH:mm:ss"),bool b=>b?"Evet":"Hayır",_=>value?.ToString()??""};sb.Append($"<c r=\"{Col(col)}{row}\" t=\"inlineStr\" s=\"{style}\"><is><t>{Esc(s)}</t></is></c>");}
    private static string Col(int index){var s="";for(var n=index+1;n>0;n=(n-1)/26)s=(char)('A'+(n-1)%26)+s;return s;}
    private static string Esc(string? s)=>SecurityElement.Escape(s??"")??"";
    private static void Add(ZipArchive zip,string path,string text){var e=zip.CreateEntry(path,CompressionLevel.Fastest);using var w=new StreamWriter(e.Open(),new UTF8Encoding(false));w.Write(text);}
}
