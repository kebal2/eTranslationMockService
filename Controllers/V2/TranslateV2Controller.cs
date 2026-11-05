using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using eTranslationMockService.Services;

using Microsoft.AspNetCore.Mvc;

namespace eTranslationMockService.Controllers.V2;

[ApiController]
[Route("/api/v2/[controller]")]
public class TranslateController : ControllerBase
{
    private readonly ICallbackService callbackService;
    private readonly IRequestLog requestLog;

    public TranslateController(ICallbackService callbackService, IRequestLog requestLog)
    {
        this.callbackService = callbackService;
        this.requestLog = requestLog;
    }

    [HttpPost(Name = "translate2")]
    public string Post(TranslateRequestv2? requestData)
    {
        if (requestData is null) return "{\"requestId\": -30000}";

        var random = new Random();
        var requestCode = random.Next(100000, int.MaxValue).ToString();
        var externalCode = requestData.callerInformation?.externalReference;

        var destination = requestData.deliveries?.http;
        var incoming = requestData.documentToTranslate?.document;

        try
        {
            if (string.IsNullOrEmpty(destination))
            {
                Log(requestData, requestCode, incoming, null, 0, "no delivery destination", null);

                return $"{{ \"requestId\":{requestCode} }}";
            }

            byte[] raw;
            var format = "text";
            string? fileName = null;

            if (incoming is not null)
            {
                // Documents are binary: decode to bytes, never to a string.
                raw = incoming.content.FromBase64ToBytes();
                format = (incoming.format ?? "text").ToLowerInvariant();
                fileName = incoming.filename;
            }
            else if (requestData.textToTranslate is not null)
            {
                raw = Encoding.UTF8.GetBytes(requestData.textToTranslate);
            }
            else throw new InvalidOperationException("(documentToTranslate && textToTranslate) is null");

            var translated = TranslationMock.Translate(raw, format, fileName, requestData.targetLanguages);

            Debug.WriteLine(translated.Content);

            this.callbackService.AddDataToSend(translated.IsText
                ? new TextCallbackRequest(new Uri(destination), translated.Content, requestCode, externalCode, requestData.sourceLanguage, requestData.targetLanguages)
                : new DocumentCallbackRequest(new Uri(destination), translated.Content, requestCode, externalCode, requestData.sourceLanguage, requestData.targetLanguages, translated.OutputFormat));

            Log(requestData, requestCode, incoming, translated, raw.Length, "queued", null);
        }
        catch (Exception ex)
        {
            Log(requestData, requestCode, incoming, null, 0, "failed", ex.Message);

            throw;
        }

        return $"{{ \"requestId\":{requestCode} }}";
    }

    private void Log(
        TranslateRequestv2 request,
        string requestCode,
        DocumentContent? incoming,
        TranslationMock.Result? translated,
        int inputBytes,
        string status,
        string? error) =>
        this.requestLog.Add(new RequestLogEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.Now,
            requestCode,
            request.sourceLanguage,
            request.targetLanguages ?? Array.Empty<string>(),
            request.callerInformation?.externalReference,
            incoming is not null ? "document" : "text",
            incoming?.filename,
            incoming?.format ?? (request.textToTranslate is not null ? "text" : null),
            inputBytes,
            request.deliveries?.http,
            translated?.OutputFormat,
            translated is null ? 0 : ResultSize(translated),
            status,
            error,
            Describe(request)));

    /// <summary>
    /// Base64 carries three bytes per four characters, and <see cref="Base64Helper"/> breaks
    /// it into lines, which are not part of the payload.
    /// </summary>
    private static int ResultSize(TranslationMock.Result translated) =>
        translated.IsText
            ? translated.Content.Length
            : (int)(translated.Content.Count(c => !char.IsWhiteSpace(c)) * 3L / 4);

    /// <summary>
    /// The request as the service understood it, with the document content left out:
    /// a few megabytes of base64 would drown the page.
    /// </summary>
    private static string Describe(TranslateRequestv2 request)
    {
        var document = request.documentToTranslate?.document;

        return JsonSerializer.Serialize(new
        {
            request.callerInformation,
            documentToTranslate = document is null
                ? null
                : new { document = new { content = $"<base64, {document.content?.Length ?? 0} chars>", document.format, document.filename } },
            request.textToTranslate,
            request.sourceLanguage,
            request.targetLanguages,
            request.domain,
            request.outputFormat,
            request.withQualityEstimate,
            request.useLLM,
            request.preserveTags,
            request.addDisclaimer,
            request.deliveries,
            request.notifications
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
