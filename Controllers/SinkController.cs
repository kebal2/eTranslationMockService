using System.Text;
using System.Text.Json;

using eTranslationMockService.Services;

using Microsoft.AspNetCore.Mvc;

namespace eTranslationMockService.Controllers;

/// <summary>
/// The callback destination the built in test page points the mock at, plus the
/// endpoints that page uses to show what arrived. Not part of the eTranslation API.
/// </summary>
[ApiController]
[Route("/api/sink")]
public class SinkController : ControllerBase
{
    private readonly ICallbackSink sink;

    public SinkController(ICallbackSink sink)
    {
        this.sink = sink;
    }

    /// <summary>
    /// Receives the JSON callback the mock posts. A body that is not JSON is kept as text,
    /// so an unexpected callback shape is still visible on the page instead of being dropped.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        string? requestId = null;
        string? sourceLanguage = null;
        string? targetLanguage = null;
        string? externalReference = null;
        string? outputFormat = null;
        string? text = null;
        byte[]? document = null;

        if (TryParseJson(body, out var json))
        {
            requestId = Read(json, "requestId");
            sourceLanguage = Read(json, "sourceLanguage");
            targetLanguage = Read(json, "targetLanguage");
            externalReference = Read(json, "externalReference");
            outputFormat = Read(json, "outputFormat");
            text = Read(json, "translatedText");

            var result = Read(json, "result");
            if (!string.IsNullOrEmpty(result)) document = Decode(result);
        }
        else if (!string.IsNullOrWhiteSpace(body))
        {
            text = body;
        }

        this.sink.Add(new SinkEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.Now,
            requestId,
            sourceLanguage,
            targetLanguage,
            externalReference,
            string.IsNullOrEmpty(outputFormat) ? null : outputFormat,
            text,
            document,
            Request.ContentType ?? string.Empty,
            body));

        return Ok(new { received = true });
    }

    [HttpGet]
    public IActionResult List() => Ok(this.sink.All().Select(CallbackView.Project));

    [HttpGet("{id}/download")]
    public IActionResult Download(string id)
    {
        var entry = this.sink.Find(id);

        if (entry?.Document is null) return NotFound();

        return File(entry.Document, CallbackView.MediaType(entry.OutputFormat), CallbackView.FileName(entry));
    }

    [HttpDelete]
    public IActionResult Clear()
    {
        this.sink.Clear();

        return Ok(new { cleared = true });
    }

    private static bool TryParseJson(string body, out JsonElement json)
    {
        json = default;

        if (string.IsNullOrWhiteSpace(body) || body.TrimStart()[0] != '{') return false;

        try
        {
            json = JsonDocument.Parse(body).RootElement;

            return json.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Read(JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ToString()
            : null;

    private static byte[]? Decode(string content)
    {
        try
        {
            return content.FromBase64ToBytes();
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
