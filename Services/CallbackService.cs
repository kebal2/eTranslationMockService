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

    /// <summary>
    /// The delivery still waiting for <paramref name="requestCode"/>, null once it went out.
    /// </summary>
    PendingDelivery? Pending(string requestCode);
}

/// <summary>
/// Posts the callbacks when the <see cref="DeliverySchedule"/> says they are due, so the
/// delays follow the level the status endpoint reports.
/// </summary>
public class CallbackService : ICallbackService, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IServiceStatus status;
    private readonly ILogger<CallbackService> logger;
    private readonly DeliverySchedule schedule = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Task worker;

    public CallbackService(IHttpClientFactory httpClientFactory, IServiceStatus status, ILogger<CallbackService> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.status = status;
        this.logger = logger;

        worker = Task.Run(SendData);
    }

    public void AddDataToSend(ICallbackRequest callbackRequest)
    {
        var delivery = schedule.Add(callbackRequest, status.Level, DateTimeOffset.Now);

        logger.LogInformation(
            "Callback for request {RequestCode} queued at {Level}, {Due}",
            callbackRequest.RequestCode,
            delivery.QueuedAt,
            delivery.DueAt is { } due ? $"due at {due:HH:mm:ss}" : "held until the service is unblocked");
    }

    public PendingDelivery? Pending(string requestCode) => schedule.Find(requestCode);

    private async Task SendData()
    {
        while (!stopping.IsCancellationRequested)
        {
            foreach (var callbackRequest in schedule.TakeDue(status.Level, DateTimeOffset.Now))
            {
                await Send(callbackRequest);
            }

            try
            {
                await Task.Delay(PollInterval, stopping.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <summary>
    /// One unreachable destination must not stop the deliveries queued behind it.
    /// </summary>
    private async Task Send(ICallbackRequest callbackRequest)
    {
        using var httpClient = httpClientFactory.CreateClient();

        foreach (var targetLanguage in callbackRequest.TargetLanguages)
        {
            try
            {
                using HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, callbackRequest.Uri)
                {
                    Content = BuildContent(callbackRequest, targetLanguage)
                };

                using var response = await httpClient.SendAsync(httpRequestMessage, stopping.Token);

                var result = await response.Content.ReadAsStringAsync(stopping.Token);
                logger.LogInformation("Response from {Uri}: {Result}", callbackRequest.Uri, result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stopping.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Callback for request {RequestCode} to {Uri} failed", callbackRequest.RequestCode, callbackRequest.Uri);
            }
        }
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

    public void Dispose()
    {
        stopping.Cancel();

        try
        {
            worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        stopping.Dispose();
        GC.SuppressFinalize(this);
    }
}
