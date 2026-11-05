using Xunit;

namespace eTranslationMockService.Tests;

public class OpenXmlHelperTests
{
    private const string Marker = "should be translated to [HU]";

    [Fact]
    public void LooksLikeOpenXml_recognises_the_zip_header_only()
    {
        Assert.True(OpenXmlHelper.LooksLikeOpenXml(TestDocuments.Docx()));
        Assert.False(OpenXmlHelper.LooksLikeOpenXml(TestDocuments.Pdf()));
        Assert.False(OpenXmlHelper.LooksLikeOpenXml(Array.Empty<byte>()));
    }

    [Fact]
    public void TryAppendMarker_keeps_the_original_content_and_adds_the_marker()
    {
        Assert.True(OpenXmlHelper.TryAppendMarker(TestDocuments.Docx(), Marker, out var result));

        var xml = TestDocuments.DocumentXml(result);

        Assert.Contains(TestDocuments.OriginalDocxText, xml);
        Assert.Contains(Marker, xml);
    }

    [Fact]
    public void TryAppendMarker_inserts_before_the_section_properties()
    {
        // w:sectPr has to stay the last child of w:body or Word reports a corrupt file.
        Assert.True(OpenXmlHelper.TryAppendMarker(TestDocuments.Docx(), Marker, out var result));

        var xml = TestDocuments.DocumentXml(result);

        Assert.True(xml.IndexOf(Marker, StringComparison.Ordinal) < xml.IndexOf("<w:sectPr", StringComparison.Ordinal));
    }

    [Fact]
    public void TryAppendMarker_appends_at_the_end_of_a_body_without_section_properties()
    {
        Assert.True(OpenXmlHelper.TryAppendMarker(TestDocuments.Docx(withSectionProperties: false), Marker, out var result));

        var xml = TestDocuments.DocumentXml(result);

        Assert.True(xml.IndexOf(Marker, StringComparison.Ordinal) < xml.IndexOf("</w:body>", StringComparison.Ordinal));
    }

    [Fact]
    public void TryAppendMarker_escapes_xml_special_characters()
    {
        Assert.True(OpenXmlHelper.TryAppendMarker(TestDocuments.Docx(), "a & b <c>", out var result));

        var xml = TestDocuments.DocumentXml(result);

        Assert.Contains("a &amp; b &lt;c&gt;", xml);
        Assert.DoesNotContain("<c>", xml);
    }

    [Fact]
    public void TryAppendMarker_declines_a_package_that_is_not_a_word_document()
    {
        Assert.False(OpenXmlHelper.TryAppendMarker(TestDocuments.Xlsx(), Marker, out var result));
        Assert.Empty(result);
    }

    [Fact]
    public void TryAppendMarker_leaves_the_other_package_entries_untouched()
    {
        Assert.True(OpenXmlHelper.TryAppendMarker(TestDocuments.Docx(), Marker, out var result));

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(result));

        Assert.Contains(zip.Entries, e => e.FullName == "[Content_Types].xml");
        Assert.Contains(zip.Entries, e => e.FullName == "_rels/.rels");
        Assert.Contains(zip.Entries, e => e.FullName == "word/document.xml");
    }
}
