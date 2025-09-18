using System.Diagnostics;
using System.Xml.Linq;
using eTranslationMockService.Services;
using Microsoft.AspNetCore.Mvc;

namespace eTranslationMockService.Controllers.V1;

[ApiController]
[Route("/api/v1/[controller]")]
public class TranslateController : ControllerBase
{
    private readonly ICallbackService callbackService;

    public TranslateController(ICallbackService callbackService)
    {
        this.callbackService = callbackService;
    }

    [HttpPost(Name = "translate1")]
    public string Post(TranslateRequestv1? requestData)
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
                    content = DocumentHelper.HandlePDF(requestData.targetLanguages, decoded).ToBase64();
                    break;
                }
                case "application/pdf":
                {
                    content = DocumentHelper.HandlePDF(requestData.targetLanguages, decoded).ToBase64();
                    break;
                }
                default:
                    break;
            }

            Debug.WriteLine(content);

            if (format == "text")
                this.callbackService.AddDataToSend(new TextCallbackRequest(new Uri(destination), content, requestCode, requestData.targetLanguages, 1));
            else
                this.callbackService.AddDataToSend(new DocumentCallbackRequest(new Uri(destination), content, requestCode, requestData.targetLanguages, 1));
        }

        return requestCode;
    }
}