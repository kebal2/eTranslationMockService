namespace TranslateMock_dotnet;

public record CallerInformation(string application);

public record Destinations(string[] httpDestinations);

public record Document(string content, string format, string filename);

public record TranslateRequest(
    Document? documentToTranslateBase64,
    string? textToTranslate,
    string sourceLanguage,
    string[] targetLanguages,
    string? errorCallback,
    CallerInformation callerInformation,
    Destinations? destinations,
    string? requesterCallback,
    string? domain,
    string? externalReference
    );
