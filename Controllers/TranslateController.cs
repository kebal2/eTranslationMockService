using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Web;
using System.Xml.Linq;

using Microsoft.AspNetCore.Mvc;

using Spire.Pdf;
using Spire.Pdf.Graphics;

using TranslateMock_dotnet.Services;

namespace TranslateMock_dotnet.Controllers;

[ApiController]
[Route("/api/v1/[controller]")]
public class TranslateController : ControllerBase
{
    private readonly ICallbackService callbackService;

    public TranslateController(ICallbackService callbackService)
    {
        this.callbackService = callbackService;
    }

    [HttpPost(Name = "translate")]
    public string Post(TranslateRequest? requestData)
    {
        if (requestData is null) return "-30000";

        var random = new Random();
        var requestCode = random.Next(100000, int.MaxValue).ToString();
        var dests = Array.Empty<string>();
        if (requestData.destinations is not null || !string.IsNullOrEmpty(requestData.requesterCallback)) dests = requestData.destinations?.httpDestinations ?? new[] { requestData.requesterCallback };

        foreach (var destination in dests)
        {
            string content;
            string decoded;
            string format = "text";
            if (requestData.documentToTranslateBase64 is not null)
            {
                content = requestData.documentToTranslateBase64.content;
                decoded = content.FromBase64();
                format = requestData.documentToTranslateBase64.format.ToLower();
            }
            else if (requestData.textToTranslate is not null)
            {
                decoded = content = requestData.textToTranslate;
            }
            else throw new InvalidOperationException();

            switch (format)
            {
                case "text":
                    content = $"{decoded} - should be translated to [{string.Join(", ", requestData.targetLanguages)}]";
                    break;
                case "html":
                    content = $"{decoded}<h1>Should be translated to [{string.Join(", ", requestData.targetLanguages)}]</h1>".ToBase64();
                    break;
                case "xhtml":
                    {
                        // ids mezők elemeinek ahol van tartalom adat hozáfűzése
                        var xhtml = XElement.Parse(decoded, LoadOptions.PreserveWhitespace);

                        foreach (var elem in xhtml.Elements())
                        {
                            elem.Value = $"{string.Join(", ", requestData.targetLanguages)} - {elem.Value}";
                        }

                        content = xhtml.ToString().ToBase64();

                        break;
                    }

                case "xml":
                    {
                        // mezők tartalmához hozzáfűzni nyelvkódot
                        var xml = XElement.Parse(decoded);

                        foreach (var elem in xml.Elements())
                        {
                            elem.Value = $"{string.Join(", ", requestData.targetLanguages)} - {elem.Value}";
                        }

                        content = xml.ToString().ToBase64();

                        break;
                    }
                case "pdf":
                    {
                        content = HandlePDF(requestData, decoded).ToBase64();
                        break;
                    }
                case "application/pdf":
                    {
                        content = HandlePDF(requestData, decoded).ToBase64();
                        break;
                    }
                default:
                    break;
            }

            Debug.WriteLine(content);

            if (format == "text")
                callbackService.AddDataToSend(new TextCallbackRequest(new Uri(destination), content, requestCode, requestData.targetLanguages));
            else
                callbackService.AddDataToSend(new DocumentCallbackRequest(new Uri(destination), content, requestCode, requestData.targetLanguages));
        }

        return requestCode;
    }

    private static byte[] HandlePDF(TranslateRequest requestData, string decoded)
    {        
        var pdf = new PdfDocument(Encoding.ASCII.GetBytes(decoded));
        PdfPageBase page = pdf.Pages.Add();

        //Draw the text
        page.Canvas.DrawString($"Hello, World! Translate to [{string.Join(", ", requestData.targetLanguages)}]",
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

internal static class Base64Helper
{
    public static string ToBase64(this string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }

    public static string ToBase64(this byte[] data)
    {
        return Convert.ToBase64String(data);
    }

    public static string FromBase64(this string text)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(text));
    }
}
