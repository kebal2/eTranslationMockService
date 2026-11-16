using eTranslationMockService.Services;

using Microsoft.AspNetCore.Mvc;

namespace eTranslationMockService.Controllers;

/// <summary>
/// What the test page follows: every request the mock handled, with the callbacks
/// it delivered for it. Not part of the eTranslation API.
/// </summary>
[ApiController]
[Route("/api/activity")]
public class ActivityController : ControllerBase
{
    private readonly IRequestLog requestLog;
    private readonly ICallbackSink sink;
    private readonly ICallbackService callbackService;

    public ActivityController(IRequestLog requestLog, ICallbackSink sink, ICallbackService callbackService)
    {
        this.requestLog = requestLog;
        this.sink = sink;
        this.callbackService = callbackService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var callbacks = this.sink.All();
        var byRequest = callbacks
            .Where(callback => callback.RequestId is not null)
            .ToLookup(callback => callback.RequestId!);

        var requests = this.requestLog.All().Select(entry => new
        {
            id = entry.Id,
            receivedAt = entry.ReceivedAt,
            requestId = entry.RequestId,
            sourceLanguage = entry.SourceLanguage,
            targetLanguages = entry.TargetLanguages,
            externalReference = entry.ExternalReference,
            inputKind = entry.InputKind,
            fileName = entry.FileName,
            format = entry.Format,
            inputBytes = entry.InputBytes,
            destination = entry.Destination,
            outputFormat = entry.OutputFormat,
            resultBytes = entry.ResultBytes,
            status = entry.Status,
            error = entry.Error,
            requestJson = entry.RequestJson,
            delivery = Delivery(entry.RequestId),
            callbacks = byRequest[entry.RequestId].Select(CallbackView.Project)
        });

        // Callbacks that belong to no logged request: posted straight to the sink,
        // or sent before the service was restarted.
        var known = this.requestLog.All().Select(entry => entry.RequestId).ToHashSet();
        var orphans = callbacks
            .Where(callback => callback.RequestId is null || !known.Contains(callback.RequestId))
            .Select(CallbackView.Project);

        return Ok(new { requests, orphans });
    }

    /// <summary>
    /// When the callback of a queued request is due, or that it is held by a blocked
    /// service. Null once it went out.
    /// </summary>
    private object? Delivery(string requestId) =>
        this.callbackService.Pending(requestId) is { } pending
            ? new { queuedAtLevel = (int)pending.QueuedAt, dueAt = pending.DueAt, held = pending.DueAt is null }
            : null;

    [HttpDelete]
    public IActionResult Clear()
    {
        this.requestLog.Clear();
        this.sink.Clear();

        return Ok(new { cleared = true });
    }
}
