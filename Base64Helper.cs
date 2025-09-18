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
        return Encoding.UTF8.GetString(Convert.FromBase64String(text));
    }
}