using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace eTranslationMockService.Tests;

/// <summary>
/// The callback sink behind the built in test page: it has to accept every callback
/// shape the mock sends, because that is the only thing the page can show.
/// </summary>
public class SinkEndpointTests : IClassFixture<MockServiceFactory>
{
    private readonly MockServiceFactory factory;

    public SinkEndpointTests(MockServiceFactory factory)
    {
        this.factory = factory;
    }

    private async Task<HttpClient> FreshClient()
    {
        var client = this.factory.CreateClient();
        using var cleared = await client.DeleteAsync("/api/sink");

        return client;
    }

    private static async Task<JsonElement> Single(HttpClient client)
    {
        var entries = await client.GetFromJsonAsync<JsonElement>("/api/sink");

        Assert.Equal(1, entries.GetArrayLength());

        return entries[0];
    }

    [Fact]
    public async Task The_console_page_is_served_from_the_root()
    {
        using var client = this.factory.CreateClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("eTranslation Mock Console", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_v2_document_callback_is_recorded_with_its_format_and_size()
    {
        using var client = await FreshClient();

        var document = TestDocuments.Pdf();

        using var posted = await client.PostAsJsonAsync("/api/sink", new
        {
            requestId = "4711",
            sourceLanguage = "EN",
            targetLanguage = "HU",
            result = document.ToBase64(),
            externalReference = "ext-1",
            outputFormat = "pdf"
        });

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var entry = await Single(client);

        Assert.Equal("4711", entry.GetProperty("requestId").GetString());
        Assert.Equal("HU", entry.GetProperty("targetLanguage").GetString());
        Assert.Equal("pdf", entry.GetProperty("outputFormat").GetString());
        Assert.Equal("ext-1", entry.GetProperty("externalReference").GetString());
        Assert.Equal(document.Length, entry.GetProperty("documentBytes").GetInt32());
    }

    [Fact]
    public async Task A_recorded_document_can_be_downloaded_unchanged()
    {
        using var client = await FreshClient();

        var document = TestDocuments.Pdf();

        using var posted = await client.PostAsJsonAsync("/api/sink", new
        {
            requestId = "4711",
            targetLanguage = "HU",
            result = document.ToBase64(),
            outputFormat = "pdf"
        });

        var id = (await Single(client)).GetProperty("id").GetString();

        using var download = await client.GetAsync($"/api/sink/{id}/download");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("HU-4711.pdf", download.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(document, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_textual_result_is_previewed_inline()
    {
        using var client = await FreshClient();

        using var posted = await client.PostAsJsonAsync("/api/sink", new
        {
            requestId = "4711",
            targetLanguage = "HU",
            result = "<p>Hello</p><h1>Should be translated to [HU]</h1>".ToBase64(),
            outputFormat = "html"
        });

        var entry = await Single(client);

        Assert.Equal("<p>Hello</p><h1>Should be translated to [HU]</h1>", entry.GetProperty("preview").GetString());
    }

    [Fact]
    public async Task A_pdf_is_not_previewed_as_text()
    {
        using var client = await FreshClient();

        using var posted = await client.PostAsJsonAsync("/api/sink", new
        {
            requestId = "4711",
            targetLanguage = "HU",
            result = TestDocuments.Pdf().ToBase64(),
            outputFormat = "pdf"
        });

        var entry = await Single(client);

        Assert.Equal(JsonValueKind.Null, entry.GetProperty("preview").ValueKind);
    }

    [Fact]
    public async Task A_v2_text_callback_is_recorded_as_text()
    {
        using var client = await FreshClient();

        using var posted = await client.PostAsJsonAsync("/api/sink", new
        {
            requestId = "4711",
            sourceLanguage = "EN",
            targetLanguage = "HU",
            translatedText = "Hello world - should be translated to [HU]"
        });

        var entry = await Single(client);

        Assert.Equal("Hello world - should be translated to [HU]", entry.GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("documentBytes").ValueKind);
    }

    [Fact]
    public async Task Callbacks_are_listed_newest_first_and_can_be_cleared()
    {
        using var client = await FreshClient();

        foreach (var language in new[] { "HU", "DE" })
        {
            using var posted = await client.PostAsJsonAsync("/api/sink", new { requestId = "4711", targetLanguage = language, translatedText = language });
        }

        var entries = await client.GetFromJsonAsync<JsonElement>("/api/sink");

        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("DE", entries[0].GetProperty("targetLanguage").GetString());

        using var cleared = await client.DeleteAsync("/api/sink");

        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/sink")).GetArrayLength());
    }

    [Fact]
    public async Task Downloading_an_unknown_callback_answers_not_found()
    {
        using var client = await FreshClient();

        using var response = await client.GetAsync("/api/sink/does-not-exist/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
