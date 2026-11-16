namespace eTranslationMockService.Services;

/// <summary>
/// When a pending callback goes out: at a time picked from the status level when it was
/// queued, or not at all while the service is blocked.
/// </summary>
/// <param name="DueAt">Null while the callback is held by a blocked service.</param>
public record PendingDelivery(string RequestCode, StatusLevel QueuedAt, DateTimeOffset? DueAt);

/// <summary>
/// The callbacks waiting to be delivered, with the delays eTranslation shows under load:
/// none to speak of in normal service, about a minute under heavy load, minutes under
/// critical load, and nothing at all while blocked.
/// </summary>
public class DeliverySchedule
{
    private readonly object gate = new();
    private readonly List<(ICallbackRequest Request, PendingDelivery Delivery)> pending = new();
    private readonly Random random;

    public DeliverySchedule(Random? random = null)
    {
        this.random = random ?? Random.Shared;
    }

    public PendingDelivery Add(ICallbackRequest request, StatusLevel level, DateTimeOffset now)
    {
        var delivery = new PendingDelivery(request.RequestCode, level, DueAt(level, now));

        lock (gate) pending.Add((request, delivery));

        return delivery;
    }

    /// <summary>
    /// Takes the callbacks that are due. A blocked service delivers nothing; once it is
    /// unblocked, the callbacks it held are scheduled at the level it is back to.
    /// </summary>
    public IReadOnlyList<ICallbackRequest> TakeDue(StatusLevel level, DateTimeOffset now)
    {
        if (level == StatusLevel.Blocked) return Array.Empty<ICallbackRequest>();

        lock (gate)
        {
            for (var i = 0; i < pending.Count; i++)
            {
                if (pending[i].Delivery.DueAt is null)
                {
                    pending[i] = (pending[i].Request, pending[i].Delivery with { DueAt = DueAt(level, now) });
                }
            }

            var due = pending.Where(entry => entry.Delivery.DueAt <= now).ToList();

            pending.RemoveAll(entry => entry.Delivery.DueAt <= now);

            return due.Select(entry => entry.Request).ToList();
        }
    }

    public PendingDelivery? Find(string requestCode)
    {
        lock (gate) return pending.Select(entry => entry.Delivery).FirstOrDefault(delivery => delivery.RequestCode == requestCode);
    }

    public int Count
    {
        get { lock (gate) return pending.Count; }
    }

    private DateTimeOffset? DueAt(StatusLevel level, DateTimeOffset now) =>
        Delay(level, random) is { } delay ? now + delay : null;

    /// <summary>
    /// How long a callback queued at <paramref name="level"/> takes; null when it waits for
    /// the service to be unblocked.
    /// </summary>
    public static TimeSpan? Delay(StatusLevel level, Random random) => level switch
    {
        StatusLevel.Normal => TimeSpan.FromMilliseconds(random.Next(0, 500)),
        StatusLevel.HeavyLoad => TimeSpan.FromSeconds(random.Next(50, 101)),
        StatusLevel.CriticalLoad => TimeSpan.FromSeconds(random.Next(120, 301)),
        StatusLevel.Blocked => null,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };
}
