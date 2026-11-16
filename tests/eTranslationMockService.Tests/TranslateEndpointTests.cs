using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

using eTranslationMockService.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace eTranslationMockService.Tests;

/// <summary>
/// Hosts the service in memory with the callback delivery replaced by a recorder,
/// so the controller wiring is exercised without a real HTTP destination.
/// </summary>
public class MockServiceFactory : WebApplicationFactory<Program>
{
    public RecordingCallbackService Callbacks { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICallbackService>();
            services.AddSingleton<ICallbackService>(Callbacks);
        });
    }
}

public class RecordingCallbackService : ICallbackService
{
    public ConcurrentQueue<ICallbackRequest> Requests { get; } = new();

    public void AddDataToSend(ICallbackRequest callbackRequest) => Requests.Enqueue(callbackRequest);

    public PendingDelivery? Pending(string requestCode) => null;
}

public class TranslateEndpointTests : IClassFixture<MockServiceFactory>
{
    private readonly MockServiceFactory factory;

    public TranslateEndpointTests(MockServiceFactory factory)
    {
        this.factory = factory;
    }

    private static object DocumentRequest(byte[] document, string format, string fileName, string? destination = "http://localhost:9999/callback") =>
        new
        {
            callerInformation = new { username = "tester", externalReference = "ext-1" },
            documentToTranslate = new { document = new { content = document.ToBase64(), format, filename = fileName } },
            sourceLanguage = "EN",
            targetLanguages = new[] { "HU" },
            notifications = new { },
            deliveries = new { http = destination }
        };

    private async Task<(int Status, string Body, ICallbackRequest? Callback)> Post(string route, object request)
    {
        this.factory.Callbacks.Requests.Clear();

        using var client = this.factory.CreateClient();
        using var response = await client.PostAsJsonAsync(route, request);

        var body = await response.Content.ReadAsStringAsync();
        this.factory.Callbacks.Requests.TryDequeue(out var callback);

        return ((int)response.StatusCode, body, callback);
    }

    [Fact]
    public async Task V2_answers_with_a_request_id()
    {
        var (status, body, _) = await Post("/api/v2/Translate", DocumentRequest(TestDocuments.Pdf(), "pdf", "sample.pdf"));

        Assert.Equal(200, status);

        using var payload = JsonDocument.Parse(body);

        Assert.True(payload.RootElement.GetProperty("requestId").GetInt64() > 0);
    }

    [Fact]
    public async Task V2_queues_a_stamped_pdf_for_delivery()
    {
        var (_, _, callback) = await Post("/api/v2/Translate", DocumentRequest(TestDocuments.Pdf(), "pdf", "sample.pdf"));

        var document = Assert.IsType<DocumentCallbackRequest>(callback);

        Assert.Equal("pdf", document.OutputFormat);
        Assert.Equal("ext-1", document.externalCode);
        Assert.Equal("EN", document.SourceLanguage);
        Assert.Equal(new[] { "HU" }, document.TargetLanguages);

        var pages = TestDocuments.PdfPageTexts(document.TranslatedDocumentsBase64.FromBase64ToBytes());

        Assert.All(pages, page => Assert.True(TestDocuments.ContainsText(page, "should be translated to [HU]"), page));

        // The caller must get its own document back, not a generated stand-in: the content
        // only survives if the document travelled as bytes all the way from the request.
        Assert.Contains(pages, page => TestDocuments.ContainsText(page, TestDocuments.OriginalPdfText));
    }

    [Fact]
    public async Task V2_queues_a_docx_for_a_word_document()
    {
        var format = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        var (_, _, callback) = await Post("/api/v2/Translate", DocumentRequest(TestDocuments.Docx(), format, "sample.docx"));

        var document = Assert.IsType<DocumentCallbackRequest>(callback);

        Assert.Equal("docx", document.OutputFormat);
        Assert.Contains("should be translated to [HU]", TestDocuments.DocumentXml(document.TranslatedDocumentsBase64.FromBase64ToBytes()));
    }

    [Fact]
    public async Task V2_queues_plain_text_as_a_text_callback()
    {
        var request = new
        {
            callerInformation = new { externalReference = "ext-1" },
            textToTranslate = "Hello world",
            sourceLanguage = "EN",
            targetLanguages = new[] { "HU" },
            notifications = new { },
            deliveries = new { http = "http://localhost:9999/callback" }
        };

        var (_, _, callback) = await Post("/api/v2/Translate", request);

        var text = Assert.IsType<TextCallbackRequest>(callback);

        Assert.Equal("Hello world - should be translated to [HU]", text.TranslatedText);
    }

    [Fact]
    public async Task V2_without_a_delivery_destination_queues_nothing()
    {
        var (status, body, callback) = await Post("/api/v2/Translate", DocumentRequest(TestDocuments.Pdf(), "pdf", "sample.pdf", destination: null));

        Assert.Equal(200, status);
        Assert.Contains("requestId", body);
        Assert.Null(callback);
    }

    [Fact]
    public async Task V2_with_an_empty_body_answers_with_the_error_code()
    {
        using var client = this.factory.CreateClient();
        using var response = await client.PostAsync("/api/v2/Translate", new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        Assert.Contains("-30000", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_v1_route_is_gone()
    {
        // The real service stopped offering v1, so the mock must not answer it either.
        using var client = this.factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/Translate", new { textToTranslate = "Hello" });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
