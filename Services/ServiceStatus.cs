namespace eTranslationMockService.Services;

/// <summary>
/// The health levels eTranslation reports at /api/status, with the message it sends for each.
/// </summary>
public enum StatusLevel
{
    Normal = 0,
    HeavyLoad = 1,
    CriticalLoad = 2,
    Blocked = 3
}

public interface IServiceStatus
{
    StatusLevel Level { get; set; }
}

/// <summary>
/// The level the mock reports. Starts at <c>Status:Level</c> from configuration (normal
/// service when unset) and can be changed at runtime, so a client's reaction to load
/// can be tested.
/// </summary>
public class ServiceStatus : IServiceStatus
{
    private volatile int level;

    public ServiceStatus(IConfiguration configuration)
    {
        var configured = configuration.GetValue<int?>("Status:Level") ?? 0;

        this.level = Enum.IsDefined(typeof(StatusLevel), configured) ? configured : 0;
    }

    public StatusLevel Level
    {
        get => (StatusLevel)this.level;
        set => this.level = (int)value;
    }

    public static string Message(StatusLevel level) => level switch
    {
        StatusLevel.Normal => "Normal service",
        StatusLevel.HeavyLoad => "Heavy load",
        StatusLevel.CriticalLoad => "Critical load (expect delays)",
        StatusLevel.Blocked => "Blocked",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };
}
