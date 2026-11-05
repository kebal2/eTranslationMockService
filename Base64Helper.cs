using System.Text;

namespace eTranslationMockService;

internal static class Base64Helper
{
    public static string ToBase64(this string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text), Base64FormattingOptions.InsertLineBreaks);
    }

    public static string ToBase64(this byte[] data)
    {
        return Convert.ToBase64String(data, Base64FormattingOptions.InsertLineBreaks);
    }

    public static string FromBase64(this string text)
    {
        return Encoding.UTF8.GetString(text.FromBase64ToBytes());
    }

    /// <summary>
    /// Binary safe counterpart of <see cref="FromBase64"/>: documents must never be routed
    /// through a UTF-8 string, that silently corrupts every byte the encoding cannot map.
    /// Tolerates line breaks, data URI prefixes and the URL safe alphabet.
    /// </summary>
    public static byte[] FromBase64ToBytes(this string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();

        var payload = text;

        var dataUri = payload.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (dataUri >= 0) payload = payload[(dataUri + "base64,".Length)..];

        payload = payload.Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace(" ", string.Empty)
            .Replace('-', '+')
            .Replace('_', '/');

        if (payload.Length % 4 != 0) payload = payload.PadRight(payload.Length + (4 - payload.Length % 4), '=');

        return Convert.FromBase64String(payload);
    }
}
