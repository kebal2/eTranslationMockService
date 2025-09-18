using System.Drawing;
using System.Text;
using Spire.Pdf;
using Spire.Pdf.Graphics;

namespace eTranslationMockService;

internal static class DocumentHelper
{
    internal static byte[] HandlePDF(string[] targetLanguages, string decoded)
    {
        var pdf = new PdfDocument(Encoding.ASCII.GetBytes(decoded));
        PdfPageBase page = pdf.Pages.Add();

        //Draw the text
        page.Canvas.DrawString($"Hello, World! Translate to [{string.Join(", ", targetLanguages)}]",
            new PdfFont(PdfFontFamily.Helvetica, 30f),
            new PdfSolidBrush(Color.Black),
            10, 10);

        using var ms = new MemoryStream();

        pdf.SaveToFile($"./test_{DateTime.Now.Ticks}.pdf");

        pdf.SaveToStream(ms, FileFormat.DOCX);
        System.IO.File.WriteAllBytes($"./test_{DateTime.Now.Ticks}.docx", ms.ToArray());

        return ms.ToArray();
    }
}