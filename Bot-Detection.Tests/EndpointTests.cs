using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bot_Detection_Service;
using BotDetection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bot_Detection.Tests;

public class ScoreEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ScoreEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private sealed record ScoreResponse(string SessionId, double Score, string Action, string[] Reasons);

    private static BotFeatures CleanHumanFeatures() => new()
    {
        SessionDurationMs = 45_000,
        TimeToFirstInteractionMs = 1_200,
        ClickCount = 3,
        FocusOrderLength = 2,
        Environment = new EnvironmentFeatures { LanguagesCount = 2, HasPlugins = true },
        Mouse = new PointerFeatures
        {
            SampleCount = 240,
            VelocityCv = 0.75,
            DirectionChangeRate = 0.22,
            StraightLineRatio = 0.10,
        },
        Touch = new PointerFeatures { SampleCount = 0 },
        Keyboard = new KeyboardFeatures { SampleCount = 18, DwellCv = 0.45, FlightCv = 0.62 },
        Scroll = new ScrollFeatures { SampleCount = 9, DeltaCv = 0.55 },
    };

    /// <summary>Scores exactly 0.20: no pointer input, everything else clean.</summary>
    private static BotFeatures NoPointerOnlyFeatures() => new()
    {
        SessionDurationMs = 45_000,
        TimeToFirstInteractionMs = 1_200,
        ClickCount = 3,
        FocusOrderLength = 2,
        Environment = new EnvironmentFeatures { LanguagesCount = 2, HasPlugins = true },
        Mouse = new PointerFeatures { SampleCount = 0 },
        Touch = new PointerFeatures { SampleCount = 0 },
        Keyboard = new KeyboardFeatures { SampleCount = 0 },
        Scroll = new ScrollFeatures { SampleCount = 0 },
    };

    private static BotFeatures ObviousBotFeatures() => new()
    {
        SessionDurationMs = 150,
        TimeToFirstInteractionMs = null,
        ClickCount = 1,
        FocusOrderLength = 1,
        Environment = new EnvironmentFeatures { Webdriver = true, LanguagesCount = 0 },
        Mouse = new PointerFeatures { SampleCount = 0 },
        Touch = new PointerFeatures { SampleCount = 0 },
        Keyboard = new KeyboardFeatures { SampleCount = 0 },
        Scroll = new ScrollFeatures { SampleCount = 0 },
    };

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Score_CleanHumanSession_ReturnsAllow()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/score", new ScoreRequest("session-clean", CleanHumanFeatures()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ScoreResponse>();
        Assert.NotNull(body);
        Assert.Equal("session-clean", body!.SessionId);
        Assert.Equal("Allow", body.Action);
        Assert.Empty(body.Reasons);
    }

    [Fact]
    public async Task Score_ObviousBot_ReturnsBlockWithReasons()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/score", new ScoreRequest("session-bot", ObviousBotFeatures()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ScoreResponse>();
        Assert.NotNull(body);
        Assert.Equal("Block", body!.Action);
        Assert.NotEmpty(body.Reasons);
    }

    [Fact]
    public async Task Score_MissingFeatures_Returns400WithFeaturesError()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/score", new { sessionId = "session-1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await ErrorKeysContain(response, "features"));
    }

    [Fact]
    public async Task Score_BlankSessionId_Returns400WithSessionIdError()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/score", new ScoreRequest("   ", CleanHumanFeatures()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await ErrorKeysContain(response, "sessionId"));
    }

    [Fact]
    public async Task Score_NullFeatureSubObject_Returns400WithThatKey()
    {
        var client = _factory.CreateClient();

        var payload = """
            {
              "sessionId": "session-1",
              "features": {
                "environment": null,
                "mouse": {}, "touch": {}, "keyboard": {}, "scroll": {}
              }
            }
            """;

        var response = await client.PostAsync(
            "/api/score", new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await ErrorKeysContain(response, "features.environment"));
    }

    [Fact]
    public async Task Score_MalformedJson_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/score", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Score_UsesConfiguredThresholds()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RiskScoring:ChallengeThreshold"] = "0.10",
                    ["RiskScoring:BlockThreshold"] = "0.15",
                })));

        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/score", new ScoreRequest("session-cfg", NoPointerOnlyFeatures()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ScoreResponse>();
        Assert.NotNull(body);
        Assert.Equal(0.2, body!.Score, precision: 3);
        Assert.Equal("Block", body.Action);
    }

    private static async Task<bool> ErrorKeysContain(HttpResponseMessage response, string key)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("errors", out var errors)
            && errors.TryGetProperty(key, out _);
    }
}