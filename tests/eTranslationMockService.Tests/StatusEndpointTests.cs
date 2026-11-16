using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace eTranslationMockService.Tests;

/// <summary>
/// The system status endpoint: anonymous, GET or POST, one of four fixed levels.
/// </summary>
public class StatusEndpointTests : IClassFixture<MockServiceFactory>
{
    private readonly MockServiceFactory factory;

    public StatusEndpointTests(MockServiceFactory factory)
    {
        this.factory = factory;
    }

    private async Task<HttpClient> FreshClient()
    {
        var client = this.factory.CreateClient();
        using var reset = await client.PutAsJsonAsync("/api/status", new { level = 0 });

        return client;
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Reports_normal_service_by_default()
    {
        // A host of its own: the shared one may have been set to another level already.
        await using var fresh = new MockServiceFactory();
        using var client = fresh.CreateClient();
        using var response = await client.GetAsync("/api/status");

        var status = await Read(response);

        Assert.Equal(0, status.GetProperty("level").GetInt32());
        Assert.Equal("Normal service", status.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Answers_POST_the_same_as_GET()
    {
        using var client = await FreshClient();
        using var response = await client.PostAsync("/api/status", null);

        var status = await Read(response);

        Assert.Equal(0, status.GetProperty("level").GetInt32());
        Assert.Equal("Normal service", status.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(0, "Normal service")]
    [InlineData(1, "Heavy load")]
    [InlineData(2, "Critical load (expect delays)")]
    [InlineData(3, "Blocked")]
    public async Task Reports_the_level_it_was_set_to(int level, string message)
    {
        using var client = await FreshClient();
        using var set = await client.PutAsJsonAsync("/api/status", new { level });
        using var response = await client.GetAsync("/api/status");

        var status = await Read(response);

        Assert.Equal(level, status.GetProperty("level").GetInt32());
        Assert.Equal(message, status.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Rejects_an_unknown_level_and_keeps_the_current_one()
    {
        using var client = await FreshClient();
        using var set = await client.PutAsJsonAsync("/api/status", new { level = 4 });

        Assert.Equal(HttpStatusCode.BadRequest, set.StatusCode);

        using var response = await client.GetAsync("/api/status");

        Assert.Equal(0, (await Read(response)).GetProperty("level").GetInt32());
    }
}
