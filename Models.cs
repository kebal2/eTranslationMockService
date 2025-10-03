namespace eTranslationMockService;

public record CallerInformation(string? username, string? departmentNumber, string? institution, string? externalReference);

public record Destinations(string[] httpDestinations);

public record Documentv1(string content, string format, string? filename);
public record Documentv2(string? ftp, string? http, Documentv1? document);

public record Destination(string? ftp, string? http);
public record Notification(Destination? success, Destination? failure);

public record TranslateRequestv1(
    Documentv1? documentToTranslateBase64,
    string? textToTranslate,
    string sourceLanguage,
    string[] targetLanguages,
    string? errorCallback,
    CallerInformation callerInformation,
    Destinations? destinations,
    string? requesterCallback,
    string? domain,
    string? externalReference);

public record TranslateRequestv2(
    CallerInformation callerInformation,
    Documentv2? documentToTranslate,
    string? textToTranslate,
    Documentv2? glossary,
    string sourceLanguage,
    string[] targetLanguages,
    string? domain,
    bool? withQualityEstimate,
    bool? useLLM,
    string? outputFormat,
    bool? preserveTags,
    bool? addDisclaimer,
    Notification notifications,
    Destination deliveries
);