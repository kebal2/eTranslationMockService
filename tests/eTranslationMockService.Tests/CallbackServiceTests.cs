using System.Text.Json;

using eTranslationMockService.Services;

using Xunit;

namespace eTranslationMockService.Tests;

public class CallbackServiceTests
{
    private static readonly string[] Hungarian = { "HU" };
    private static readonly Uri Destination = new("http://localhost:9999/callback?token=abc");

    private static JsonElement Payload(ICallbackRequest request, string targetLanguage = "HU")
    {
        var content = CallbackService.BuildContent(request, targetLanguage);

        Assert.Equal("application/json", content.Headers.ContentType?.MediaType);

        return JsonDocument.Parse(content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
    }

    [Fact]
    public void A_text_callback_is_posted_as_json()
    {
        var request = new TextCallbackRequest(Destination, "translated", "4711", "ext-1", "EN", Hungarian);

        var payload = Payload(request);

        Assert.Equal("4711", payload.GetProperty("requestId").GetString());
        Assert.Equal("EN", payload.GetProperty("sourceLanguage").GetString());
        Assert.Equal("HU", payload.GetProperty("targetLanguage").GetString());
        Assert.Equal("translated", payload.GetProperty("translatedText").GetString());
        Assert.Equal("ext-1", payload.GetProperty("externalReference").GetString());
    }

    [Fact]
    public void A_document_callback_reports_the_output_format()
    {
        var request = new DocumentCallbackRequest(Destination, "ZG9j", "4711", "ext-1", "EN", Hungarian, "docx");

        var payload = Payload(request);

        Assert.Equal("ZG9j", payload.GetProperty("result").GetString());
        Assert.Equal("docx", payload.GetProperty("outputFormat").GetString());
        Assert.Equal("ext-1", payload.GetProperty("externalReference").GetString());
    }

    [Fact]
    public void The_source_language_of_the_request_is_reported_rather_than_a_fixed_one()
    {
        var request = new DocumentCallbackRequest(Destination, "ZG9j", "4711", null, "DE", Hungarian, "pdf");

        Assert.Equal("DE", Payload(request).GetProperty("sourceLanguage").GetString());
    }

    [Fact]
    public void A_missing_output_format_is_reported_as_an_empty_string()
    {
        var request = new DocumentCallbackRequest(Destination, "ZG9j", "4711", null, "EN", Hungarian);

        Assert.Equal(string.Empty, Payload(request).GetProperty("outputFormat").GetString());
    }

    [Fact]
    public void Every_target_language_gets_its_own_callback_body()
    {
        var request = new TextCallbackRequest(Destination, "translated", "4711", null, "EN", new[] { "HU", "DE" });

        Assert.Equal("HU", Payload(request, "HU").GetProperty("targetLanguage").GetString());
        Assert.Equal("DE", Payload(request, "DE").GetProperty("targetLanguage").GetString());
    }
}
