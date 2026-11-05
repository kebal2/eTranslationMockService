using System.IO.Compression;
using System.Security;
using System.Text;

namespace eTranslationMockService;

/// <summary>
/// Minimal .docx editing without any external dependency: a .docx is a zip, so the
/// marker paragraph is injected straight into word/document.xml. Keeps the incoming
/// document intact, which a generated PDF would not.
/// </summary>
internal static class OpenXmlHelper
{
    private const string DocumentEntry = "word/document.xml";

    internal static bool LooksLikeOpenXml(byte[] data) =>
        data.Length > 4 && data[0] == 'P' && data[1] == 'K' && data[2] == 3 && data[3] == 4;

    internal static bool TryAppendMarker(byte[] original, string marker, out byte[] result)
    {
        result = Array.Empty<byte>();

        using var ms = new MemoryStream();
        ms.Write(original, 0, original.Length);
        ms.Position = 0;

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(DocumentEntry);
            if (entry is null) return false;

            string xml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                xml = reader.ReadToEnd();
            }

            if (!TryInject(xml, marker, out var updated)) return false;

            entry.Delete();
            var replacement = zip.CreateEntry(DocumentEntry, CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
            writer.Write(updated);
        }

        result = ms.ToArray();

        return true;
    }

    private static bool TryInject(string documentXml, string marker, out string updated)
    {
        updated = documentXml;

        var paragraph = "<w:p><w:r><w:rPr><w:color w:val=\"C00000\"/><w:b/></w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{SecurityElement.Escape(marker)}</w:t></w:r></w:p>";

        // The section properties have to stay the last child of the body, so insert before them.
        var insertAt = documentXml.LastIndexOf("<w:sectPr", StringComparison.Ordinal);
        if (insertAt < 0) insertAt = documentXml.LastIndexOf("</w:body>", StringComparison.Ordinal);
        if (insertAt < 0) return false;

        updated = documentXml.Insert(insertAt, paragraph);

        return true;
    }
}
