using Bot_Detection_Service;
using Bot_Detection_Service.Models;
using Bot_Detection_Service.Validators;

namespace BotDetection.Tests;

/// <summary>
/// Unit tests for the structural request validation that guards
/// POST /api/score. Like BotScorer, ScoreRequestValidator.Validate is a pure
/// function (request in, error dictionary out), so it's tested directly here
/// without the web host; the HTTP wiring (that a 400 actually comes back) is
/// covered separately by the endpoint-layer tests.
/// </summary>
public class ScoreRequestValidatorTests
{
    // A fully-populated, valid request other tests mutate one field of.
    private static ScoreRequest ValidRequest() => new(
        SessionId: "session-abc",
        Features: new BotFeatures
        {
            Environment = new EnvironmentFeatures(),
            Mouse = new MouseFeatures(),
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
    public void Validate_NullRequestPattern_IsStillValid()
    {
        // RequestPattern is optional by design — its absence must not error.
        var req = ValidRequest() with { Features = new BotFeatures { RequestPattern = null } };

        var errors = ScoreRequestValidator.Validate(req);

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
    public void Validate_NullClientId_IsAllowed()
    {
        var req = ValidRequest();

        var errors = ScoreRequestValidator.Validate(req);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NullFeatures_ReportsFeaturesError()
    {
        var req = ValidRequest() with { Features = null! };

        var errors = ScoreRequestValidator.Validate(req);

        Assert.True(errors.ContainsKey("features"));
    }

    [Fact]
    public void Validate_ExplicitNullSubObject_ReportsThatSubObject()
    {
        // The scorer dereferences each sub-object; an explicit JSON null
        // (which System.Text.Json binds despite the non-null annotation) must
        // be caught here rather than null-ref'ing inside Score().
        var req = ValidRequest() with
        {
            Features = new BotFeatures
            {
                Environment = null!,
                Mouse = new MouseFeatures(),
                Keyboard = new KeyboardFeatures(),
                Scroll = new ScrollFeatures(),
            },
        };

        var errors = ScoreRequestValidator.Validate(req);

        Assert.True(errors.ContainsKey("features.environment"));
        Assert.False(errors.ContainsKey("features.mouse"));
    }
}