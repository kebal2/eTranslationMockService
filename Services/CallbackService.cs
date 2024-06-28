using System.Collections.Concurrent;
using System.Text;
using System.Web;

namespace TranslateMock_dotnet.Services;

public interface ICallbackRequest
{
    Uri Uri { get; }
    string RequestCode { get; }
    string? ExternalReference { get; }
    string[] TargetLanguages { get; }
}

public record DocumentCallbackRequest(Uri Uri, string TranslatedDocumentsBase64, string RequestCode, string? ExternalReference, string[] TargetLanguages) : ICallbackRequest;

public record TextCallbackRequest(Uri Uri, string TranslatedText, string RequestCode, string? ExternalReference, string[] TargetLanguages) : ICallbackRequest;



public interface ICallbackService
{
    void AddDataToSend(DocumentCallbackRequest documentCallbackRequest);
    void AddDataToSend(TextCallbackRequest textCallbackRequest);
}

public class CallbackService : ICallbackService, IDisposable
{
    private readonly IHttpClientFactory httpClientFactory;
    private static readonly ConcurrentQueue<ICallbackRequest> DataToSend = new();

    private bool running;
    private bool disposing;
    private bool isDisposed;

    public CallbackService(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;

        Task.Run(() => SendData());
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
                using var httpClient = httpClientFactory.CreateClient();
                foreach (var targetLanguage in callbackRequest.TargetLanguages)
                {
                    var query = HttpUtility.ParseQueryString(callbackRequest.Uri.Query);
                    query["request-id"] = callbackRequest.RequestCode;

                    if(!string.IsNullOrEmpty(callbackRequest.ExternalReference))
                        query["external-reference"] = callbackRequest.ExternalReference;

                    query["target-language"] = targetLanguage;

                    StringContent sc = null;

                    switch (callbackRequest)
                    {
                        case TextCallbackRequest tcr:
                            query["translated-text"] = tcr.TranslatedText;
                            break;
                        case DocumentCallbackRequest dcr:
                            sc = new StringContent(dcr.TranslatedDocumentsBase64, Encoding.UTF8);
                            break;
                    }

                    UriBuilder uriBuilder = new(callbackRequest.Uri)
                    {
                        Query = query.ToString()
                    };

                    using HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, uriBuilder.Uri);

                    if (callbackRequest is DocumentCallbackRequest)
                        httpRequestMessage.Content = sc;


                    var response = httpClient.Send(httpRequestMessage);

                    var result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Console.WriteLine($"Response from {callbackRequest.Uri}: {result}");
                }
            }

            Thread.Sleep(1000);
        }

        running = false;
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
