using System.Text;

using Xunit;

namespace eTranslationMockService.Tests;

public class Base64HelperTests
{
    [Fact]
    public void FromBase64ToBytes_round_trips_binary_content()
    {
        var original = TestDocuments.Pdf();

        var decoded = original.ToBase64().FromBase64ToBytes();

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void FromBase64_corrupts_binary_content_so_documents_must_use_the_byte_overload()
    {
        // Guards the regression this helper exists for: a PDF routed through a UTF-8
        // string comes back as replacement characters.
        var original = TestDocuments.Pdf();

        var viaString = Encoding.UTF8.GetBytes(original.ToBase64().FromBase64());

        Assert.NotEqual(original, viaString);
    }

    [Fact]
    public void FromBase64ToBytes_accepts_the_line_breaks_ToBase64_inserts()
    {
        var original = new byte[300];
        Random.Shared.NextBytes(original);

        var encoded = original.ToBase64();

        Assert.Contains("\r\n", encoded);
        Assert.Equal(original, encoded.FromBase64ToBytes());
    }

    [Fact]
    public void FromBase64ToBytes_accepts_a_data_uri_prefix()
    {
        var encoded = "data:application/pdf;base64," + Convert.ToBase64String("hello"u8.ToArray());

        Assert.Equal("hello"u8.ToArray(), encoded.FromBase64ToBytes());
    }

    [Fact]
    public void FromBase64ToBytes_accepts_the_url_safe_alphabet_without_padding()
    {
        var original = new byte[] { 0xFB, 0xEF, 0xBE, 0xFF };

        var urlSafe = Convert.ToBase64String(original).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Assert.Equal(original, urlSafe.FromBase64ToBytes());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void FromBase64ToBytes_treats_missing_content_as_empty(string? content)
    {
        Assert.Empty(content!.FromBase64ToBytes());
    }
}
