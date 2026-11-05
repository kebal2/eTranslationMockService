using System.Text;
using System.Xml.Linq;

using Xunit;

namespace eTranslationMockService.Tests;

public class TranslationMockTests
{
    private static readonly string[] Hungarian = { "HU" };

    private static TranslationMock.Result Translate(string text, string format) =>
        TranslationMock.Translate(Encoding.UTF8.GetBytes(text), format, "sample." + format, Hungarian);

    [Theory]
    [InlineData("text")]
    [InlineData("txt")]
    [InlineData("text/plain")]
    [InlineData("")]
    public void Plain_text_is_appended_to_and_sent_as_text(string format)
    {
        var result = Translate("Hello world", format);

        Assert.True(result.IsText);
        Assert.Equal("Hello world - should be translated to [HU]", result.Content);
    }

    [Fact]
    public void Html_gets_a_heading_and_is_sent_base64_encoded()
    {
        var result = Translate("<p>Hello</p>", "html");

        Assert.False(result.IsText);
        Assert.Equal("html", result.OutputFormat);
        Assert.Equal("<p>Hello</p><h1>Should be translated to [HU]</h1>", result.Content.FromBase64());
    }

    [Fact]
    public void Xml_elements_are_prefixed_with_the_target_languages()
    {
        var result = Translate("<root><a>alpha</a><b>beta</b></root>", "xml");

        Assert.False(result.IsText);
        Assert.Equal("xml", result.OutputFormat);

        var root = XElement.Parse(result.Content.FromBase64());

        Assert.Equal("HU - alpha", root.Element("a")!.Value);
        Assert.Equal("HU - beta", root.Element("b")!.Value);
    }

    [Fact]
    public void Xhtml_elements_are_prefixed_with_the_target_languages()
    {
        var result = Translate("<div><p>alpha</p></div>", "xhtml");

        Assert.Equal("xhtml", result.OutputFormat);
        Assert.Equal("HU - alpha", XElement.Parse(result.Content.FromBase64()).Element("p")!.Value);
    }

    [Fact]
    public void Multiple_target_languages_are_listed_in_the_marker()
    {
        var result = TranslationMock.Translate(Encoding.UTF8.GetBytes("Hello"), "text", null, new[] { "HU", "DE", "FR" });

        Assert.Equal("Hello - should be translated to [HU, DE, FR]", result.Content);
    }

    [Fact]
    public void A_pdf_is_returned_as_a_base64_pdf()
    {
        var result = TranslationMock.Translate(TestDocuments.Pdf(), "pdf", "sample.pdf", Hungarian);

        Assert.False(result.IsText);
        Assert.Equal("pdf", result.OutputFormat);

        var pages = TestDocuments.PdfPageTexts(result.Content.FromBase64ToBytes());

        Assert.All(pages, page => Assert.True(TestDocuments.ContainsText(page, "should be translated to [HU]"), page));
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/msword")]
    public void A_word_document_is_returned_as_a_base64_docx(string format)
    {
        var result = TranslationMock.Translate(TestDocuments.Docx(), format, "sample.docx", Hungarian);

        Assert.False(result.IsText);
        Assert.Equal("docx", result.OutputFormat);
        Assert.Contains("should be translated to [HU]", TestDocuments.DocumentXml(result.Content.FromBase64ToBytes()));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("application/pdf")]
    [InlineData("xlsx")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("application/octet-stream")]
    public void Document_formats_are_never_routed_through_the_text_path(string format)
    {
        var result = TranslationMock.Translate(TestDocuments.Pdf(), format, "sample", Hungarian);

        Assert.False(result.IsText);
    }

    [Fact]
    public void An_unknown_format_holding_binary_content_is_detected_from_its_bytes()
    {
        // The caller may label a document with anything; the magic bytes decide.
        var result = TranslationMock.Translate(TestDocuments.Pdf(), "application/x-made-up", "sample", Hungarian);

        Assert.False(result.IsText);
        Assert.Equal("pdf", result.OutputFormat);
    }

    [Fact]
    public void An_unknown_format_holding_text_is_appended_to_like_text()
    {
        var result = Translate("id;name", "csv");

        Assert.True(result.IsText);
        Assert.Equal("csv", result.OutputFormat);
        Assert.Equal("id;name - should be translated to [HU]", result.Content);
    }
}
