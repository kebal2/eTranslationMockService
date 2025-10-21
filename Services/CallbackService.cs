using System.Collections.Concurrent;
using System.Text;
using System.Web;

namespace eTranslationMockService.Services;

public interface ICallbackRequest
{
    Uri Uri { get; }
    string RequestCode { get; }
    string SourceLanguage { get; }
    string[] TargetLanguages { get; }
    int Version { get; }
}

public record DocumentCallbackRequest(
    Uri Uri,
    string TranslatedDocumentsBase64,
    string RequestCode,
    string? externalCode,
    string SourceLanguage,
    string[] TargetLanguages,
    int Version) : ICallbackRequest;

public record TextCallbackRequest(
    Uri Uri,
    string TranslatedText,
    string RequestCode,
    string? externalCode,
    string SourceLanguage,
    string[] TargetLanguages,
    int Version) : ICallbackRequest;

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
                    StringContent? sc = null;

                    var uriBuilder = BuildContent(callbackRequest, targetLanguage, ref sc);

                    using HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, uriBuilder.Uri);

                    if (sc is not null)
                        httpRequestMessage.Content = sc;

                    using var response = httpClient.Send(httpRequestMessage);

                    var result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Console.WriteLine($"Response from {callbackRequest.Uri}: {result}");
                }
            }

            Thread.Sleep(200);
        }

        running = false;
    }

    private static UriBuilder BuildContent(ICallbackRequest callbackRequest, string targetLanguage, ref StringContent? sc)
    {
        var query = HttpUtility.ParseQueryString(callbackRequest.Uri.Query);

        switch (callbackRequest)
        {
            case TextCallbackRequest tcr:
            {
                if (tcr.Version == 1)
                {
                    query["request-id"] = callbackRequest.RequestCode;
                    query["target-language"] = targetLanguage;
                    query["translated-text"] = tcr.TranslatedText;
                    query["external-reference"] = tcr.externalCode;
                }
                else
                {
                    sc = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        requestId = callbackRequest.RequestCode,
                        sourceLanguage = "en",
                        targetLanguage = targetLanguage,
                        translatedText = tcr.TranslatedText,
                        externalReference = tcr.externalCode
                    }));
                }

                break;
            }
            case DocumentCallbackRequest dcr:
            {
                if (dcr.Version == 1)
                {
                    query["request-id"] = callbackRequest.RequestCode;
                    query["target-language"] = targetLanguage;
                    query["external-reference"] = dcr.externalCode;
                    sc = new StringContent(dcr.TranslatedDocumentsBase64, Encoding.UTF8);
                }
                else
                {
                    sc = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        requestId = callbackRequest.RequestCode,
                        sourceLanguage = "en",
                        targetLanguage = targetLanguage,
                        result = dcr.TranslatedDocumentsBase64,
                        externalReference = dcr.externalCode,
                        outputFormat = ""
                    }));
                }

                break;
            }
        }

        UriBuilder uriBuilder = new(callbackRequest.Uri)
        {
            Query = query.ToString()
        };
        return uriBuilder;
    }

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