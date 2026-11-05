using System.Text;

using Xunit;

namespace eTranslationMockService.Tests;

public class DocumentHelperTests
{
    private static readonly string[] Hungarian = { "HU" };
    private static readonly string[] TwoLanguages = { "HU", "DE" };

    [Fact]
    public void A_pdf_comes_back_as_a_pdf_that_still_contains_the_original_text()
    {
        var (content, format) = DocumentHelper.CreateTranslatedDocument(TestDocuments.Pdf(), Hungarian, "sample.pdf", "pdf");

        Assert.Equal("pdf", format);

        var pages = TestDocuments.PdfPageTexts(content);

        Assert.Contains(pages, page => TestDocuments.ContainsText(page, $"{TestDocuments.OriginalPdfText} 1"));
    }

    [Fact]
    public void Every_copied_page_carries_the_marker()
    {
        var (content, _) = DocumentHelper.CreateTranslatedDocument(TestDocuments.Pdf(pages: 3), TwoLanguages, "sample.pdf", "pdf");

        var pages = TestDocuments.PdfPageTexts(content);

        // The three copied pages plus the summary page.
        Assert.Equal(4, pages.Count);
        Assert.All(pages, page => Assert.True(
            TestDocuments.ContainsText(page, "should be translated to [HU, DE]"),
            $"The marker is missing from a page: '{page}'"));
    }

    [Fact]
    public void The_summary_page_names_the_incoming_file()
    {
        var (content, _) = DocumentHelper.CreateTranslatedDocument(TestDocuments.Pdf(), Hungarian, "sample.pdf", "pdf");

        var summary = TestDocuments.PdfPageTexts(content).Last();

        Assert.True(TestDocuments.ContainsText(summary, "sample.pdf"), $"The summary page does not name the file: '{summary}'");
    }

    [Fact]
    public void The_stamp_is_written_in_the_page_coordinate_system()
    {
        // Regression: QuestPDF leaves a 0.25 scaled, y-flipped CTM behind, so a stamp appended
        // after the page content used to be drawn mirrored, quarter sized and off position.
        var (content, _) = DocumentHelper.CreateTranslatedDocument(TestDocuments.Pdf(), Hungarian, "sample.pdf", "pdf");

        using var document = UglyToad.PdfPig.PdfDocument.Open(content);

        var page = document.GetPage(1);
        var markerLetters = page.Letters.Where(letter => letter.FontName?.Contains("Helvetica", StringComparison.OrdinalIgnoreCase) == true).ToList();

        Assert.NotEmpty(markerLetters);
        Assert.All(markerLetters, letter =>
        {
            Assert.InRange(letter.PointSize, 11, 13);                     // 12pt, not 3pt
            Assert.InRange(letter.Location.Y, 0, page.Height / 2);        // near the bottom, not off the top
            Assert.InRange(letter.Location.X, 0, page.Width);
        });
    }

    [Fact]
    public void A_filename_outside_latin1_does_not_break_the_stamp()
    {
        var (content, format) = DocumentHelper.CreateTranslatedDocument(TestDocuments.Pdf(), Hungarian, "árvíztűrő-日本語.pdf", "pdf");

        Assert.Equal("pdf", format);
        Assert.NotEmpty(TestDocuments.PdfPageTexts(content));
    }

    [Fact]
    public void A_docx_comes_back_as_a_docx_with_the_marker_injected()
    {
        var (content, format) = DocumentHelper.CreateTranslatedDocument(TestDocuments.Docx(), Hungarian, "sample.docx", "docx");

        Assert.Equal("docx", format);

        var xml = TestDocuments.DocumentXml(content);

        Assert.Contains(TestDocuments.OriginalDocxText, xml);
        Assert.Contains("should be translated to [HU]", xml);
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("pptx")]
    public void A_package_we_cannot_rewrite_falls_back_to_a_generated_pdf(string sourceFormat)
    {
        var (content, format) = DocumentHelper.CreateTranslatedDocument(TestDocuments.Xlsx(), Hungarian, "sample." + sourceFormat, sourceFormat);

        Assert.Equal("pdf", format);

        var text = string.Join(" ", TestDocuments.PdfPageTexts(content));

        Assert.True(TestDocuments.ContainsText(text, "should be translated to [HU]"), text);
        Assert.True(TestDocuments.ContainsText(text, "sample." + sourceFormat), text);
    }

    [Fact]
    public void A_legacy_office_document_falls_back_to_a_generated_pdf()
    {
        var (content, format) = DocumentHelper.CreateTranslatedDocument(TestDocuments.LegacyOfficeDocument(), Hungarian, "sample.doc", "doc");

        Assert.Equal("pdf", format);
        Assert.True(TestDocuments.ContainsText(string.Join(" ", TestDocuments.PdfPageTexts(content)), "should be translated to [HU]"));
    }

    [Fact]
    public void A_broken_pdf_falls_back_to_a_generated_pdf_instead_of_throwing()
    {
        var broken = Encoding.ASCII.GetBytes("%PDF-1.7 and then nothing that parses");

        var (content, format) = DocumentHelper.CreateTranslatedDocument(broken, Hungarian, "broken.pdf", "pdf");

        Assert.Equal("pdf", format);
        Assert.True(TestDocuments.ContainsText(string.Join(" ", TestDocuments.PdfPageTexts(content)), "should be translated to [HU]"));
    }

    [Fact]
    public void An_empty_document_still_produces_a_readable_pdf()
    {
        var (content, format) = DocumentHelper.CreateTranslatedDocument(Array.Empty<byte>(), Hungarian, null, null);

        Assert.Equal("pdf", format);
        Assert.Equal(1, TestDocuments.PdfPageCount(content));
    }

    [Fact]
    public void IsBinaryDocument_recognises_the_formats_that_must_not_be_treated_as_text()
    {
        Assert.True(DocumentHelper.IsBinaryDocument(TestDocuments.Pdf()));
        Assert.True(DocumentHelper.IsBinaryDocument(TestDocuments.Docx()));
        Assert.True(DocumentHelper.IsBinaryDocument(TestDocuments.LegacyOfficeDocument()));
        Assert.False(DocumentHelper.IsBinaryDocument(Encoding.UTF8.GetBytes("plain text")));
        Assert.False(DocumentHelper.IsBinaryDocument(Array.Empty<byte>()));
    }

    [Fact]
    public void Nothing_is_written_to_disk_unless_the_dump_directory_is_set()
    {
        var before = Directory.GetFiles(Directory.GetCurrentDirectory());

        DocumentHelper.DumpIfRequested(TestDocuments.Pdf(), "sample.pdf", "pdf");

        Assert.Equal(before, Directory.GetFiles(Directory.GetCurrentDirectory()));
    }

    [Fact]
    public void The_dump_directory_receives_the_document_under_a_sanitized_name()
    {
        var dump = Path.Combine(Path.GetTempPath(), "etmock-dump-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ETMOCK_DUMP_DIR", dump);

        try
        {
            DocumentHelper.DumpIfRequested(TestDocuments.Pdf(), "../../escape.pdf", "pdf");

            var written = Assert.Single(Directory.GetFiles(dump));

            Assert.StartsWith("escape.pdf_", Path.GetFileName(written));
            Assert.EndsWith(".pdf", written);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ETMOCK_DUMP_DIR", null);
            if (Directory.Exists(dump)) Directory.Delete(dump, recursive: true);
        }
    }
}
