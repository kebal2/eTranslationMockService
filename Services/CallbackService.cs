using System.Collections.Concurrent;
using System.Text;

namespace eTranslationMockService.Services;

public interface ICallbackRequest
{
    Uri Uri { get; }
    string RequestCode { get; }
    string SourceLanguage { get; }
    string[] TargetLanguages { get; }
}

public record DocumentCallbackRequest(
    Uri Uri,
    string TranslatedDocumentsBase64,
    string RequestCode,
    string? externalCode,
    string SourceLanguage,
    string[] TargetLanguages,
    string? OutputFormat = null) : ICallbackRequest;

public record TextCallbackRequest(
    Uri Uri,
    string TranslatedText,
    string RequestCode,
    string? externalCode,
    string SourceLanguage,
    string[] TargetLanguages) : ICallbackRequest;

public interface ICallbackService
{
    void AddDataToSend(ICallbackRequest callbackRequest);
}

public class CallbackService : ICallbackService, IDisposable
{
    private readonly IHttpClientFactory httpClientFactory;
    private static readonly ConcurrentQueue<ICallbackRequest> dataToSend = new();
    private static readonly Random random = new();

    private bool running;
    private bool disposing;
    private bool isDisposed;

    public CallbackService(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;

        Task.Run(SendData);
    }

    public void AddDataToSend(ICallbackRequest callbackRequest)
    {
        dataToSend.Enqueue(callbackRequest);
    }

    private void SendData()
    {
        running = true;

        while (!disposing && !isDisposed)
        {
            while (dataToSend.TryDequeue(out var callbackRequest))
            {
                Thread.Sleep(random.Next(500));
                using var httpClient = httpClientFactory.CreateClient();
                foreach (var targetLanguage in callbackRequest.TargetLanguages)
                {
                    using HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, callbackRequest.Uri)
                    {
                        Content = BuildContent(callbackRequest, targetLanguage)
                    };

                    using var response = httpClient.Send(httpRequestMessage);

                    var result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Console.WriteLine($"Response from {callbackRequest.Uri}: {result}");
                }
            }

            Thread.Sleep(1000);
        }

        running = false;
    }

    /// <summary>
    /// The callback body eTranslation posts for one target language.
    /// </summary>
    internal static StringContent BuildContent(ICallbackRequest callbackRequest, string targetLanguage) =>
        callbackRequest switch
        {
            TextCallbackRequest tcr => JsonContent(new
            {
                requestId = callbackRequest.RequestCode,
                sourceLanguage = callbackRequest.SourceLanguage,
                targetLanguage = targetLanguage,
                translatedText = tcr.TranslatedText,
                externalReference = tcr.externalCode
            }),
            DocumentCallbackRequest dcr => JsonContent(new
            {
                requestId = callbackRequest.RequestCode,
                sourceLanguage = callbackRequest.SourceLanguage,
                targetLanguage = targetLanguage,
                result = dcr.TranslatedDocumentsBase64,
                externalReference = dcr.externalCode,
                outputFormat = dcr.OutputFormat ?? string.Empty
            }),
            _ => throw new InvalidOperationException($"Unknown callback request: {callbackRequest.GetType().Name}")
        };

    private static StringContent JsonContent(object payload) =>
        new(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    protected virtual void Dispose(bool d)
    {
        if (!disposing && d)
        {
            disposing = true;
            int i = 10;
            while (running && --i > 0)
            {
                Thread.Sleep(500);
            }
        }

        isDisposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}