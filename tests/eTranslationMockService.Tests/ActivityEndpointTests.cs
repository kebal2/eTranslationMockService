using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace eTranslationMockService.Tests;

/// <summary>
/// The request log behind the test page: what the page follows from the moment a
/// request arrives until its callbacks come back.
/// </summary>
public class ActivityEndpointTests : IClassFixture<MockServiceFactory>
{
    private readonly MockServiceFactory factory;

    public ActivityEndpointTests(MockServiceFactory factory)
    {
        this.factory = factory;
    }

    private async Task<HttpClient> FreshClient()
    {
        var client = this.factory.CreateClient();
        using var cleared = await client.DeleteAsync("/api/activity");

        return client;
    }

    private static async Task<JsonElement> Activity(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/activity");

    private static JsonElement SingleRequest(JsonElement activity)
    {
        var requests = activity.GetProperty("requests");

        Assert.Equal(1, requests.GetArrayLength());

        return requests[0];
    }

    private static object TextRequest(string externalReference, string? destination = "http://localhost:9999/callback") => new
    {
        callerInformation = new { externalReference },
        textToTranslate = "Hello world",
        sourceLanguage = "EN",
        targetLanguages = new[] { "HU", "DE" },
        notifications = new { },
        deliveries = new { http = destination }
    };

    [Fact]
    public async Task A_text_request_is_logged_with_what_the_mock_made_of_it()
    {
        using var client = await FreshClient();
        using var posted = await client.PostAsJsonAsync("/api/v2/Translate", TextRequest("ext-1"));

        var request = SingleRequest(await Activity(client));

        Assert.Equal("queued", request.GetProperty("status").GetString());
        Assert.Equal("text", request.GetProperty("inputKind").GetString());
        Assert.Equal("EN", request.GetProperty("sourceLanguage").GetString());
        Assert.Equal("ext-1", request.GetProperty("externalReference").GetString());
        Assert.Equal(new[] { "HU", "DE" }, request.GetProperty("targetLanguages").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal("txt", request.GetProperty("outputFormat").GetString());
        Assert.Equal("http://localhost:9999/callback", request.GetProperty("destination").GetString());
        Assert.Equal(JsonValueKind.Null, request.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task A_document_request_is_logged_with_its_file_and_result()
    {
        using var client = await FreshClient();

        var document = TestDocuments.Pdf();

        using var posted = await client.PostAsJsonAsync("/api/v2/Translate", new
        {
            callerInformation = new { externalReference = "ext-1" },
            documentToTranslate = new { document = new { content = document.ToBase64(), format = "pdf", filename = "sample.pdf" } },
            sourceLanguage = "EN",
            targetLanguages = new[] { "HU" },
            notifications = new { },
            deliveries = new { http = "http://localhost:9999/callback" }
        });

        var request = SingleRequest(await Activity(client));

        Assert.Equal("document", request.GetProperty("inputKind").GetString());
        Assert.Equal("sample.pdf", request.GetProperty("fileName").GetString());
        Assert.Equal("pdf", request.GetProperty("format").GetString());
        Assert.Equal("pdf", request.GetProperty("outputFormat").GetString());
        Assert.InRange(request.GetProperty("inputBytes").GetInt32(), document.Length - 4, document.Length);
        Assert.True(request.GetProperty("resultBytes").GetInt32() > 0);
    }

    [Fact]
    public async Task The_logged_request_body_leaves_the_document_content_out()
    {
        using var client = await FreshClient();

        using var posted = await client.PostAsJsonAsync("/api/v2/Translate", new
        {
            callerInformation = new { externalReference = "ext-1" },
            documentToTranslate = new { document = new { content = TestDocuments.Pdf().ToBase64(), format = "pdf", filename = "sample.pdf" } },
            sourceLanguage = "EN",
            targetLanguages = new[] { "HU" },
            notifications = new { },
            deliveries = new { http = "http://localhost:9999/callback" }
        });

        var body = SingleRequest(await Activity(client)).GetProperty("requestJson").GetString()!;

        Assert.Contains("<base64,", body);
        Assert.Contains("sample.pdf", body);
        Assert.DoesNotContain("JVBER", body);
    }

    [Fact]
    public async Task Callbacks_are_grouped_under_the_request_they_belong_to()
    {
        using var client = await FreshClient();

        using var posted = await client.PostAsJsonAsync("/api/v2/Translate", TextRequest("ext-1"));

        var requestId = JsonDocument.Parse(await posted.Content.ReadAsStringAsync()).RootElement.GetProperty("requestId").GetInt64().ToString();

        foreach (var language in new[] { "HU", "DE" })
        {
            using var callback = await client.PostAsJsonAsync("/api/sink", new { requestId, targetLanguage = language, translatedText = "done" });
        }

        var activity = await Activity(client);
        var request = SingleRequest(activity);

        Assert.Equal(2, request.GetProperty("callbacks").GetArrayLength());
        Assert.Equal(0, activity.GetProperty("orphans").GetArrayLength());
    }

    [Fact]
    public async Task A_callback_posted_straight_to_the_sink_is_listed_as_an_orphan()
    {
        using var client = await FreshClient();

        using var callback = await client.PostAsJsonAsync("/api/sink", new { requestId = "unknown", targetLanguage = "FR", translatedText = "direct" });

        var activity = await Activity(client);

        Assert.Equal(0, activity.GetProperty("requests").GetArrayLength());
        Assert.Equal(1, activity.GetProperty("orphans").GetArrayLength());
    }

    [Fact]
    public async Task A_request_without_a_delivery_destination_is_logged_as_such()
    {
        using var client = await FreshClient();

        using var posted = await client.PostAsJsonAsync("/api/v2/Translate", TextRequest("ext-1", destination: null));

        var request = SingleRequest(await Activity(client));

        Assert.Equal("no delivery destination", request.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, request.GetProperty("outputFormat").ValueKind);
    }

    [Fact]
    public async Task A_failing_request_is_logged_with_its_error()
    {
        using var client = await FreshClient();

        try
        {
            // Neither a document nor text to translate.
            using var posted = await client.PostAsJsonAsync("/api/v2/Translate", new
            {
                callerInformation = new { externalReference = "ext-1" },
                sourceLanguage = "EN",
                targetLanguages = new[] { "HU" },
                notifications = new { },
                deliveries = new { http = "http://localhost:9999/callback" }
            });
        }
        catch (InvalidOperationException)
        {
            // The host may surface the failure to the caller instead of answering 500.
        }

        var request = SingleRequest(await Activity(client));

        Assert.Equal("failed", request.GetProperty("status").GetString());
        Assert.Contains("textToTranslate", request.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Requests_are_listed_newest_first_and_can_be_cleared()
    {
        using var client = await FreshClient();

        foreach (var reference in new[] { "first", "second" })
        {
            using var posted = await client.PostAsJsonAsync("/api/v2/Translate", TextRequest(reference));
        }

        var requests = (await Activity(client)).GetProperty("requests");

        Assert.Equal(2, requests.GetArrayLength());
        Assert.Equal("second", requests[0].GetProperty("externalReference").GetString());

        using var cleared = await client.DeleteAsync("/api/activity");

        var empty = await Activity(client);

        Assert.Equal(0, empty.GetProperty("requests").GetArrayLength());
        Assert.Equal(0, empty.GetProperty("orphans").GetArrayLength());
    }
}
