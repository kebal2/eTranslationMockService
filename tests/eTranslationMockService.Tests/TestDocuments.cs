using System.IO.Compression;
using System.Text;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using UglyToad.PdfPig;

namespace eTranslationMockService.Tests;

/// <summary>
/// Sample payloads for the tests, built the same way the real callers build theirs.
/// </summary>
internal static class TestDocuments
{
    internal const string OriginalPdfText = "ORIGINAL PDF CONTENT";
    internal const string OriginalDocxText = "Original docx content.";

    /// <summary>
    /// A QuestPDF document: worth using as the fixture because its page stream leaves a scaled,
    /// y-flipped CTM behind, which is exactly what broke stamping before.
    /// </summary>
    internal static byte[] Pdf(int pages = 1)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        using var ms = new MemoryStream();

        Document.Create(container =>
            {
                for (var i = 1; i <= pages; i++)
                {
                    var pageNumber = i;
                    container.Page(page =>
                    {
                        page.Margin(50);
                        page.Content().Text($"{OriginalPdfText} {pageNumber}").FontColor(Colors.Black);
                    });
                }
            })
            .GeneratePdf(ms);

        return ms.ToArray();
    }

    internal static byte[] Docx(bool withSectionProperties = true)
    {
        var sectPr = withSectionProperties ? "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr>" : string.Empty;

        var document = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                       "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                       $"<w:p><w:r><w:t>{OriginalDocxText}</w:t></w:r></w:p>{sectPr}" +
                       "</w:body></w:document>";

        return Zip(new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                                      "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                                      "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                                      "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                                      "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                                      "</Types>",
            ["_rels/.rels"] = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                              "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                              "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                              "</Relationships>",
            ["word/document.xml"] = document
        });
    }

    /// <summary>An OpenXML package that is not a Word document, so it cannot be edited in place.</summary>
    internal static byte[] Xlsx() => Zip(new Dictionary<string, string>
    {
        ["[Content_Types].xml"] = "<Types/>",
        ["xl/workbook.xml"] = "<workbook/>"
    });

    /// <summary>The OLE2 compound file header of a legacy .doc/.xls/.ppt.</summary>
    internal static byte[] LegacyOfficeDocument()
    {
        var data = new byte[512];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(data, 0);

        return data;
    }

    private static byte[] Zip(Dictionary<string, string> entries)
    {
        using var ms = new MemoryStream();

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }

    internal static string DocumentXml(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);

        return reader.ReadToEnd();
    }

    internal static IReadOnlyList<string> PdfPageTexts(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);

        return document.GetPages().Select(page => page.Text).ToList();
    }

    internal static int PdfPageCount(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);

        return document.NumberOfPages;
    }

    /// <summary>
    /// Text extracted from a PDF keeps the glyphs but not necessarily the spacing,
    /// so assertions compare the whitespace free form.
    /// </summary>
    internal static string Squash(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    internal static bool ContainsText(string haystack, string needle) =>
        Squash(haystack).Contains(Squash(needle), StringComparison.Ordinal);
}
