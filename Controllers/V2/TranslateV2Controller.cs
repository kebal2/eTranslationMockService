using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Xml.Linq;
using eTranslationMockService.Controllers.V1;
using eTranslationMockService.Services;
using Microsoft.AspNetCore.Mvc;

using Spire.Pdf;
using Spire.Pdf.Graphics;

namespace eTranslationMockService.Controllers.V2;

[ApiController]
[Route("/api/v2/[controller]")]
public class TranslateController : ControllerBase
{
    private readonly ICallbackService callbackService;

    public TranslateController(ICallbackService callbackService)
    {
        this.callbackService = callbackService;
    }

    [HttpPost(Name = "translate2")]
    public string Post(TranslateRequestv2? requestData)
    {
        if (requestData is null) return "-30000";

        var random = new Random();
        var requestCode = random.Next(100000, int.MaxValue).ToString();
        string destination;
        if (requestData.deliveries is not null || !string.IsNullOrEmpty(requestData.deliveries.http)) 

        {
            destination = requestData.deliveries.http;
            string content;
            string decoded;
            string format = "text";
            if (requestData.documentToTranslate is not null)
            {
                content = requestData.documentToTranslate.document.content;
                decoded = content.FromBase64();
                format = requestData.documentToTranslate.document.format.ToLower();
            }
            else if (requestData.textToTranslate is not null)
            {
                decoded = content = requestData.textToTranslate;
            }
            else throw new InvalidOperationException("(documentToTranslate && textToTranslate) is null");

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
                this.callbackService.AddDataToSend(new TextCallbackRequest(new Uri(destination), content, requestCode, requestData.targetLanguages, 2));
            else
                this.callbackService.AddDataToSend(new DocumentCallbackRequest(new Uri(destination), content, requestCode, requestData.targetLanguages, 2));
        }

        return requestCode;
    }
}
