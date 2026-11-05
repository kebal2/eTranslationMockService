using System.Text;

using eTranslationMockService.Services;

namespace eTranslationMockService.Controllers;

/// <summary>
/// How a received callback is presented to the test page.
/// </summary>
internal static class CallbackView
{
    internal static object Project(SinkEntry entry) => new
    {
        id = entry.Id,
        receivedAt = entry.ReceivedAt,
        requestId = entry.RequestId,
        sourceLanguage = entry.SourceLanguage,
        targetLanguage = entry.TargetLanguage,
        externalReference = entry.ExternalReference,
        outputFormat = entry.OutputFormat,
        contentType = entry.ContentType,
        text = entry.Text,
        preview = Preview(entry),
        documentBytes = entry.Document?.Length,
        fileName = FileName(entry)
    };

    /// <summary>
    /// Textual results (html, xml, plain text) are worth showing inline; a pdf or docx is not.
    /// </summary>
    internal static string? Preview(SinkEntry entry)
    {
        if (entry.Document is null) return null;
        if (entry.OutputFormat is not ("html" or "xhtml" or "xml" or "txt" or null)) return null;

        var text = Encoding.UTF8.GetString(entry.Document);

        return text.Length > 4000 ? text[..4000] + "..." : text;
    }

    internal static string FileName(SinkEntry entry) =>
        $"{entry.TargetLanguage ?? "translated"}-{entry.RequestId ?? entry.Id}.{entry.OutputFormat ?? "bin"}";

    internal static string MediaType(string? outputFormat) => outputFormat switch
    {
        "pdf" => "application/pdf",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "html" => "text/html",
        "xhtml" => "application/xhtml+xml",
        "xml" => "application/xml",
        "txt" => "text/plain",
        _ => "application/octet-stream"
    };
}
