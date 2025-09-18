using System.Collections.Concurrent;
using System.Text;
using System.Web;

namespace eTranslationMockService.Services;

public interface ICallbackRequest
{
    Uri Uri { get; }
    string RequestCode { get; }
    string[] TargetLanguages { get; }
    int Version { get; }
}

public record DocumentCallbackRequest(Uri Uri, string TranslatedDocumentsBase64, string RequestCode, string[] TargetLanguages, int Version) : ICallbackRequest;

public record TextCallbackRequest(Uri Uri, string TranslatedText, string RequestCode, string[] TargetLanguages, int Version) : ICallbackRequest;

public interface ICallbackService
{
    void AddDataToSend(DocumentCallbackRequest documentCallbackRequest);
    void AddDataToSend(TextCallbackRequest textCallbackRequest);
}

public class CallbackService : ICallbackService, IDisposable
{
    private readonly IHttpClientFactory httpClientFactory;
    private static readonly ConcurrentQueue<ICallbackRequest> DataToSend = new();
    private static Random random = new();

    private bool running;
    private bool disposing;
    private bool isDisposed;

    public CallbackService(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;

        Task.Run(SendData);
    }

    public void AddDataToSend(DocumentCallbackRequest documentCallbackRequest)
    {
        DataToSend.Enqueue(documentCallbackRequest);
    }

    public void AddDataToSend(TextCallbackRequest textCallbackRequest)
    {
        DataToSend.Enqueue(textCallbackRequest);
    }

    private void SendData()
    {
        running = true;

        while (!disposing && !isDisposed)
        {
            while (DataToSend.TryDequeue(out var callbackRequest))
            {
                Thread.Sleep(random.Next(500));
                using var httpClient = httpClientFactory.CreateClient();
                foreach (var targetLanguage in callbackRequest.TargetLanguages)
                {
                    StringContent sc = null;

                    var uriBuilder = BuildContent(callbackRequest, targetLanguage, ref sc);

                    using HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, uriBuilder.Uri);

                    if (callbackRequest is DocumentCallbackRequest)
                        httpRequestMessage.Content = sc;

                    using var response = httpClient.Send(httpRequestMessage);

                    var result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Console.WriteLine($"Response from {callbackRequest.Uri}: {result}");
                }
            }

            Thread.Sleep(1000);
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
                }
                else
                {
                    sc = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        requestId = callbackRequest.RequestCode,
                        sourceLanguage = "en",
                        targetLanguage = targetLanguage,
                        translatedText = tcr.TranslatedText,
                        externalReference = ""
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
                        externalReference = "",
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

    public void Dispose()
    {
        if (!disposing)
        {
            disposing = true;
            int i = 5;
            while (running && --i > 0)
            {
                Thread.Sleep(500);
            }
        }

        isDisposed = true;
    }
}