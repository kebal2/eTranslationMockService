using System.Text;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

using PdfPageSize = UglyToad.PdfPig.Content.PageSize;

namespace eTranslationMockService;

/// <summary>
/// Builds the "translated" documents the mock sends back on the callback.
/// Everything here uses free libraries only: PdfPig (Apache-2.0) to stamp an
/// existing PDF and QuestPDF (Community) to synthesize one when the incoming
/// document cannot be edited in place.
/// </summary>
internal static class DocumentHelper
{
    private const int MarkerFontSize = 12;

    static DocumentHelper()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Returns the mock translation of <paramref name="original"/> together with
    /// the format of the result ("pdf" or "docx").
    /// </summary>
    internal static (byte[] Content, string OutputFormat) CreateTranslatedDocument(
        byte[] original,
        string[] targetLanguages,
        string? fileName,
        string? sourceFormat = null)
    {
        var marker = $"should be translated to [{string.Join(", ", targetLanguages)}]";

        if (LooksLikePdf(original))
        {
            try
            {
                return (StampPdf(original, marker, fileName), "pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not stamp the incoming PDF ({ex.Message}), falling back to a generated one.");
            }
        }

        if (OpenXmlHelper.LooksLikeOpenXml(original))
        {
            try
            {
                if (OpenXmlHelper.TryAppendMarker(original, marker, out var docx))
                    return (docx, "docx");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not edit the incoming OpenXML document ({ex.Message}), falling back to a generated PDF.");
            }
        }

        return (CreatePlaceholderPdf(marker, fileName, sourceFormat, original.Length), "pdf");
    }

    /// <summary>
    /// Copies every page of the incoming PDF and writes the marker onto each one, so the
    /// caller gets its own document back with the injected text instead of a generated stand-in.
    /// </summary>
    /// <remarks>
    /// The marker goes into a content stream placed *before* the copied page content: a page
    /// stream may leave the graphics state transformed (QuestPDF, for example, ends up with a
    /// scaled and y-flipped CTM), and anything appended after it inherits that transformation.
    /// A page whose own content paints an opaque background would cover the stamp, so the
    /// summary page added at the end guarantees the marker is visible in every document.
    /// </remarks>
    private static byte[] StampPdf(byte[] original, string marker, string? fileName)
    {
        using var input = PdfDocument.Open(original);

        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        for (var pageNumber = 1; pageNumber <= input.NumberOfPages; pageNumber++)
        {
            var page = builder.AddPage(input, pageNumber);

            page.NewContentStreamBefore();
            page.SetTextAndFillColor(200, 0, 0);
            page.AddText(Latin1(marker), MarkerFontSize, new PdfPoint(40, 28), font);
            page.ResetColor();
        }

        var summary = builder.AddPage(PdfPageSize.A4);
        summary.SetTextAndFillColor(200, 0, 0);
        summary.AddText(Latin1(marker), MarkerFontSize + 4, new PdfPoint(60, 700), font);
        summary.ResetColor();
        summary.AddText(Latin1($"{fileName ?? "document"} - eTranslationMockService"), MarkerFontSize, new PdfPoint(60, 670), font);

        return builder.Build();
    }

    /// <summary>
    /// The Standard 14 fonts only cover Latin-1, and a filename may well contain more than that.
    /// </summary>
    private static string Latin1(string text)
    {
        var latin1 = Encoding.GetEncoding("ISO-8859-1", EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);

        return latin1.GetString(latin1.GetBytes(text));
    }

    /// <summary>
    /// Last resort for formats we cannot rewrite (xlsx, pptx, legacy Office, unknown binaries):
    /// a brand new one page PDF that carries the marker and describes the input.
    /// </summary>
    private static byte[] CreatePlaceholderPdf(string marker, string? fileName, string? sourceFormat, int originalLength)
    {
        using var ms = new MemoryStream();

        Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(50);
                    page.Content().Column(column =>
                    {
                        column.Spacing(10);
                        column.Item().Text($"{fileName ?? "document"} - {marker}").FontColor(Colors.Red.Accent4).FontSize(16);
                        column.Item().Text($"Source format: {sourceFormat ?? "unknown"}");
                        column.Item().Text($"Original size: {originalLength} bytes");
                        column.Item().Text("Generated by eTranslationMockService - the original content is not preserved for this format.")
                            .FontColor(Colors.Grey.Darken1).FontSize(9);
                    });
                });
            })
            .GeneratePdf(ms);

        return ms.ToArray();
    }

    /// <summary>
    /// True when the payload is a document we must not route through a text transformation:
    /// PDF, an OpenXML package (docx/xlsx/pptx) or a legacy OLE2 Office file (doc/xls/ppt).
    /// </summary>
    internal static bool IsBinaryDocument(byte[] data) =>
        LooksLikePdf(data) || OpenXmlHelper.LooksLikeOpenXml(data) || LooksLikeOle2(data);

    private static bool LooksLikePdf(byte[] data) =>
        data.Length > 4 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F';

    private static bool LooksLikeOle2(byte[] data) =>
        data.Length > 8 && data[0] == 0xD0 && data[1] == 0xCF && data[2] == 0x11 && data[3] == 0xE0;

    /// <summary>
    /// Optional debug aid: set ETMOCK_DUMP_DIR to have every generated document written to disk.
    /// </summary>
    internal static void DumpIfRequested(byte[] content, string? fileName, string extension)
    {
        var dir = Environment.GetEnvironmentVariable("ETMOCK_DUMP_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;

        try
        {
            Directory.CreateDirectory(dir);
            var safeName = Path.GetFileName(fileName ?? "document");
            foreach (var invalid in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(invalid, '_');

            File.WriteAllBytes(Path.Combine(dir, $"{safeName}_{DateTime.Now.Ticks}.{extension}"), content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not write the document dump: {ex.Message}");
        }
    }
}
