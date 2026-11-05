using System.Collections.Concurrent;

namespace eTranslationMockService.Services;

/// <summary>
/// One callback the mock delivered to the built in test page.
/// </summary>
public record SinkEntry(
    string Id,
    DateTimeOffset ReceivedAt,
    string? RequestId,
    string? SourceLanguage,
    string? TargetLanguage,
    string? ExternalReference,
    string? OutputFormat,
    string? Text,
    byte[]? Document,
    string ContentType,
    string RawBody);

public interface ICallbackSink
{
    void Add(SinkEntry entry);
    IReadOnlyList<SinkEntry> All();
    SinkEntry? Find(string id);
    void Clear();
}

/// <summary>
/// In memory ring of the most recent callbacks, so the test page can show what came back.
/// </summary>
public class CallbackSink : ICallbackSink
{
    private const int Capacity = 50;

    private readonly ConcurrentQueue<SinkEntry> entries = new();

    public void Add(SinkEntry entry)
    {
        entries.Enqueue(entry);

        while (entries.Count > Capacity && entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<SinkEntry> All() => entries.Reverse().ToList();

    public SinkEntry? Find(string id) => entries.FirstOrDefault(entry => entry.Id == id);

    public void Clear()
    {
        while (entries.TryDequeue(out _))
        {
        }
    }
}
