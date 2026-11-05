using System.Text;
using System.Xml.Linq;

namespace eTranslationMockService;

/// <summary>
/// The actual "translation" both API versions perform: the target languages are
/// appended to the incoming content in a way that fits its format.
/// </summary>
internal static class TranslationMock
{
    internal record Result(string Content, string OutputFormat, bool IsText);

    internal static Result Translate(byte[] raw, string format, string? fileName, string[] targetLanguages)
    {
        var languages = string.Join(", ", targetLanguages);

        switch (format)
        {
            case "":
            case "text":
            case "txt":
            case "text/plain":
                return new Result($"{Decode(raw)} - should be translated to [{languages}]", "txt", true);

            case "html":
            case "text/html":
                return new Result($"{Decode(raw)}<h1>Should be translated to [{languages}]</h1>".ToBase64(), "html", false);

            case "xhtml":
            {
                // ids mezők elemeinek ahol van tartalom adat hozáfűzése
                var xhtml = XElement.Parse(Decode(raw), LoadOptions.PreserveWhitespace);

                foreach (var elem in xhtml.Elements())
                {
                    elem.Value = $"{languages} - {elem.Value}";
                }

                return new Result(xhtml.ToString().ToBase64(), "xhtml", false);
            }

            case "xml":
            {
                // mezők tartalmához hozzáfűzni nyelvkódot
                var xml = XElement.Parse(Decode(raw));

                foreach (var elem in xml.Elements())
                {
                    elem.Value = $"{languages} - {elem.Value}";
                }

                return new Result(xml.ToString().ToBase64(), "xml", false);
            }

            default:
            {
                if (!IsBinaryFormat(format) && !DocumentHelper.IsBinaryDocument(raw))
                    return new Result($"{Decode(raw)} - should be translated to [{languages}]", format, true);

                var (content, outputFormat) = DocumentHelper.CreateTranslatedDocument(raw, targetLanguages, fileName, format);

                DocumentHelper.DumpIfRequested(content, fileName, outputFormat);

                return new Result(content.ToBase64(), outputFormat, false);
            }
        }
    }

    /// <summary>
    /// The request may name the format either by extension ("docx") or by mime type
    /// ("application/vnd.openxmlformats-officedocument.wordprocessingml.document").
    /// </summary>
    private static bool IsBinaryFormat(string format) =>
        BinaryFormats.Contains(format) || BinaryFormatMarkers.Any(marker => format.Contains(marker, StringComparison.Ordinal));

    private static readonly HashSet<string> BinaryFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "doc", "docx", "dot", "dotx", "xls", "xlsx", "xlt", "xltx", "ppt", "pptx", "pps", "ppsx", "odt", "ods", "odp", "rtf"
    };

    private static readonly string[] BinaryFormatMarkers =
    {
        "pdf", "msword", "ms-word", "ms-excel", "ms-powerpoint", "officedocument", "opendocument", "octet-stream"
    };

    private static string Decode(byte[] raw) => Encoding.UTF8.GetString(raw);
}
