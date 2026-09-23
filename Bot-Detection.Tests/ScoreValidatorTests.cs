using Bot_Detection_Service;
using Xunit;

namespace BotDetection.Tests;

/// <summary>
/// Unit tests for the structural request validation that guards
/// POST /api/score. Like BotScorer, ScoreRequestValidator.Validate is a pure
/// function (request in, error dictionary out), so it's tested directly here
/// without the web host.
/// </summary>
public class ScoreRequestValidatorTests
{
    // A fully-populated, valid request that other tests mutate one field of.
    private static ScoreRequest ValidRequest() => new(
        SessionId: "session-abc",
        Features: new BotFeatures
        {
            Environment = new EnvironmentFeatures(),
            Mouse = new PointerFeatures(),
            Touch = new PointerFeatures(),
            Keyboard = new KeyboardFeatures(),
            Scroll = new ScrollFeatures(),
        });

    [Fact]
    public void Validate_FullyValidRequest_HasNoErrors()
    {
        var errors = ScoreRequestValidator.Validate(ValidRequest());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NullRequest_ReportsRequestError()
    {
        var errors = ScoreRequestValidator.Validate(null);

        Assert.True(errors.ContainsKey("request"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingSessionId_ReportsSessionIdError(string? sessionId)
    {
        var req = ValidRequest() with { SessionId = sessionId! };

        var errors = ScoreRequestValidator.Validate(req);

        Assert.True(errors.ContainsKey("sessionId"));
    }

    [Fact]
    public void Validate_OverlongSessionId_ReportsSessionIdError()
    {
        var req = ValidRequest() with
        {
            SessionId = new string('x', ScoreRequestValidator.MaxIdentifierLength + 1),
        };

        var errors = ScoreRequestValidator.Validate(req);

        Assert.True(errors.ContainsKey("sessionId"));
    }

    [Fact]
    public void Validate_NullFeatures_ReportsFeaturesError()
    {
        var req = ValidRequest() with { Features = null! };

        var errors = ScoreRequestValidator.Validate(req);

        Assert.True(errors.ContainsKey("features"));
    }

    [Theory]
    [InlineData("features.environment")]
    [InlineData("features.mouse")]
    [InlineData("features.touch")]
    [InlineData("features.keyboard")]
    [InlineData("features.scroll")]
    public void Validate_MissingSubObject_ReportsThatSubObject(string expectedKey)
    {
        // Every sub-object is required. System.Text.Json binds an explicit
        // JSON null despite the non-null annotation, and an omitted field
        // binds null too (the models seed `null!`, not `new()`), so both
        // shapes land here rather than silently scoring as zero risk.
        var features = new BotFeatures
        {
            Environment = expectedKey == "features.environment" ? null! : new EnvironmentFeatures(),
            Mouse = expectedKey == "features.mouse" ? null! : new PointerFeatures(),
            Touch = expectedKey == "features.touch" ? null! : new PointerFeatures(),
            Keyboard = expectedKey == "features.keyboard" ? null! : new KeyboardFeatures(),
            Scroll = expectedKey == "features.scroll" ? null! : new ScrollFeatures(),
        };
        var req = ValidRequest() with { Features = features };

        var errors = ScoreRequestValidator.Validate(req);

        Assert.True(errors.ContainsKey(expectedKey));
        Assert.Single(errors); // only the one we nulled out
    }
}