using System.Collections.Concurrent;

namespace eTranslationMockService.Services;

/// <summary>
/// One translation request the mock handled, as the test page shows it.
/// </summary>
public record RequestLogEntry(
    string Id,
    DateTimeOffset ReceivedAt,
    string RequestId,
    string? SourceLanguage,
    string[] TargetLanguages,
    string? ExternalReference,
    string InputKind,
    string? FileName,
    string? Format,
    int InputBytes,
    string? Destination,
    string? OutputFormat,
    int ResultBytes,
    string Status,
    string? Error,
    string RequestJson);

public interface IRequestLog
{
    void Add(RequestLogEntry entry);
    IReadOnlyList<RequestLogEntry> All();
    void Clear();
}

/// <summary>
/// In memory ring of the most recent requests, the counterpart of <see cref="CallbackSink"/>.
/// </summary>
public class RequestLog : IRequestLog
{
    private const int Capacity = 50;

    private readonly ConcurrentQueue<RequestLogEntry> entries = new();

    public void Add(RequestLogEntry entry)
    {
        entries.Enqueue(entry);

        while (entries.Count > Capacity && entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<RequestLogEntry> All() => entries.Reverse().ToList();

    public void Clear()
    {
        while (entries.TryDequeue(out _))
        {
        }
    }
}
