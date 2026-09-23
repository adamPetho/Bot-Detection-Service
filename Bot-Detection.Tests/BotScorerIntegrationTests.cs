using Bot_Detection_Service.Services;
using Xunit;

namespace BotDetection.Tests;

/// <summary>
/// End-to-end tests through the public Score() entry point. These build a
/// full BotFeatures vector representing a realistic scenario and assert on
/// the combined score, action, and reasons — i.e. what a caller of BotScorer
/// actually sees.
///
/// BotScorer.Score() is a pure function: same BotFeatures in, same
/// ScoreResult out, every time. No clock, no randomness, no I/O.
///
/// Score() assumes a fully-populated vector; at the HTTP boundary that is
/// guaranteed by ScoreRequestValidator, which rejects a request missing any
/// sub-object with a 400.
///
/// For tests that isolate a single signal category, see
/// BotScorerSignalTests.cs. For action threshold boundaries, see
/// BotScorerActionTests.cs.
/// </summary>
public class BotScorerIntegrationTests
{
    private readonly RiskCalculator _scorer = new();

    /// <summary>
    /// The clearest possible bot case: navigator.webdriver set and no mouse
    /// movement at all — the signature of an unmodified Selenium/Puppeteer
    /// bot driving a form programmatically. Every signal points the same
    /// direction, so this isn't a borderline judgment call.
    /// </summary>
    [Fact]
    public void ObviousBot_WebdriverFlagAndNoMouseMovement_RecommendsBlock()
    {
        var features = new BotFeatures
        {
            SessionDurationMs = 150,
            TimeToFirstInteractionMs = null, // no interaction recorded before submit
            ClickCount = 1,
            FocusOrderLength = 1,
            Environment = new EnvironmentFeatures
            {
                Webdriver = true,
                LanguagesCount = 0,
                HasPlugins = false,
                HasTouch = false,
                InnerW = 1920,
                InnerH = 1080,
                TimezoneOffset = 0,
            },
            Mouse = new PointerFeatures { SampleCount = 0 },
            Touch = new PointerFeatures { SampleCount = 0 },
            Keyboard = new KeyboardFeatures { SampleCount = 0 },
            Scroll = new ScrollFeatures { SampleCount = 0 },
        };

        var result = _scorer.Score(features);

        Assert.Equal(RiskAction.Block, result.Action);
        Assert.True(result.Score >= 0.6, $"Expected score >= 0.6, got {result.Score}");
        Assert.Contains(result.Reasons, r => r.Contains("webdriver", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Reasons, r => r.Contains("mouse", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The clearest possible human case: every signal clean — normal browser
    /// environment, natural pause before interacting, plenty of
    /// mouse/keyboard/scroll samples with realistic variance, and an
    /// unremarkable browsing window. Should score zero with no reasons.
    /// </summary>
    [Fact]
    public void CleanHumanSession_AllNaturalSignals_RecommendsAllow()
    {
        var features = new BotFeatures
        {
            SessionDurationMs = 45_000,
            TimeToFirstInteractionMs = 1_200,
            ClickCount = 3,
            FocusOrderLength = 2,
            Environment = new EnvironmentFeatures
            {
                Webdriver = false,
                LanguagesCount = 2,
                HasPlugins = true,
                HasTouch = false,
                InnerW = 1440,
                InnerH = 900,
                TimezoneOffset = -60,
            },
            Mouse = new PointerFeatures
            {
                SampleCount = 240,
                MeanVelocity = 0.8,
                VelocityCv = 0.75,           // well above the 0.15 CV floor
                DirectionChangeRate = 0.22,  // well above the 0.05 human floor
                StraightLineRatio = 0.10,    // well below the 0.85 bot threshold
            },
            Touch = new PointerFeatures { SampleCount = 0 },
            Keyboard = new KeyboardFeatures
            {
                SampleCount = 18,
                MeanDwellMs = 95,
                DwellCv = 0.45,
                MeanFlightMs = 140,
                FlightCv = 0.62,
            },
            Scroll = new ScrollFeatures
            {
                SampleCount = 9,
                MeanDelta = 80,
                DeltaCv = 0.55,
            },
        };

        var result = _scorer.Score(features);

        Assert.Equal(RiskAction.Allow, result.Action);
        Assert.True(result.Score < 0.3, $"Expected score < 0.3, got {result.Score}");
        Assert.Empty(result.Reasons);
    }

    /// <summary>
    /// A session with exactly two signals firing — no mouse data at all
    /// (+0.2) and an implausibly fast first interaction (+0.15) — everything
    /// else clean. That combination is fully deterministic (0.35) and lands
    /// in the Challenge band without tipping into Block, which is the
    /// scenario the middle action tier exists for: "add friction, don't
    /// hard-block."
    /// </summary>
    [Fact]
    public void MixedSignals_NoMouseDataPlusFastInteraction_RecommendsChallenge()
    {
        var features = new BotFeatures
        {
            SessionDurationMs = 5_000,       // above the 800ms "superhuman" floor, so this alone doesn't fire
            TimeToFirstInteractionMs = 20,   // < 50ms triggers the fast-interaction check
            ClickCount = 1,
            FocusOrderLength = 1,
            Environment = new EnvironmentFeatures
            {
                Webdriver = false,
                LanguagesCount = 2,
                HasPlugins = true,
                HasTouch = false,
                InnerW = 1920,
                InnerH = 1080,
                TimezoneOffset = 0,
            },
            Mouse = new PointerFeatures { SampleCount = 0 },
            Touch = new PointerFeatures { SampleCount = 0 },
            Keyboard = new KeyboardFeatures { SampleCount = 0 },
            Scroll = new ScrollFeatures { SampleCount = 0 },
        };

        var result = _scorer.Score(features);

        Assert.Equal(RiskAction.Challenge, result.Action);
        Assert.Equal(0.35, result.Score, precision: 3);
        Assert.Equal(2, result.Reasons.Count);
    }
}