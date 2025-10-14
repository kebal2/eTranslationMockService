using System.Drawing;
using System.Text;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace eTranslationMockService;

internal static class DocumentHelper
{
    internal static byte[] CreateTestPDF(string[] targetLanguages, string fileName)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        using var ms = new MemoryStream();
        Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Content()
                        .Padding(50)
                        .Text(text => { text.Span($"Hello, {fileName}! Translate to [{string.Join(", ", targetLanguages)}]").FontColor(Colors.Red.Accent4); });
                });
            })
            .GeneratePdf(ms);

        File.WriteAllBytes($"./{fileName}_{DateTime.Now.Ticks}.pdf", ms.ToArray());

        return ms.ToArray();
    }
}