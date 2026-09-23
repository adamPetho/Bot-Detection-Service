using Bot_Detection_Service.Services;
using Xunit;

namespace BotDetection.Tests;

/// <summary>
/// Isolated tests for each Check* signal method on RiskCalculator. These call the
/// internal static methods directly (exposed via [InternalsVisibleTo] in
/// BotDetection/AssemblyInfo.cs) so each signal category can be verified on
/// its own, without building a full feature vector or worrying about other
/// signals interfering.
///
/// Each test starts from a running score of 0.0 and an empty reasons list,
/// mirroring how Score() chains these calls.
/// </summary>
public class RiskCalculatorSignalTests
{
    // --- Environment --------------------------------------------------

    [Fact]
    public void CheckEnvironmentSignals_WebdriverFlagSet_Adds0_5AndReason()
    {
        var f = MakeFeatures(env: new EnvironmentFeatures { Webdriver = true, LanguagesCount = 1, HasPlugins = true });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckEnvironmentSignals(f, reasons, 0.0);

        Assert.Equal(0.5, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("webdriver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckEnvironmentSignals_NoLanguagesReported_Adds0_05()
    {
        var f = MakeFeatures(env: new EnvironmentFeatures { Webdriver = false, LanguagesCount = 0, HasPlugins = true });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckEnvironmentSignals(f, reasons, 0.0);

        Assert.Equal(0.05, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("languages", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckEnvironmentSignals_NoPluginsReported_IsNotScored()
    {
        // An empty navigator.plugins list is normal in privacy-hardened
        // browsers, so it must not cost a legitimate user anything.
        var f = MakeFeatures(env: new EnvironmentFeatures { Webdriver = false, LanguagesCount = 1, HasPlugins = false });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckEnvironmentSignals(f, reasons, 0.0);

        Assert.Equal(0.0, score, precision: 3);
        Assert.Empty(reasons);
    }

    [Fact]
    public void CheckEnvironmentSignals_AllClean_AddsNothing()
    {
        var f = MakeFeatures(env: new EnvironmentFeatures { Webdriver = false, LanguagesCount = 2, HasPlugins = true });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckEnvironmentSignals(f, reasons, 0.0);

        Assert.Equal(0.0, score, precision: 3);
        Assert.Empty(reasons);
    }

    // --- Timing --------------------------------------------------------

    [Fact]
    public void CheckTimingSignals_NoInteractionRecorded_Adds0_2()
    {
        var f = MakeFeatures(sessionDurationMs: 5_000, timeToFirstInteractionMs: null, clickCount: 1);
        var reasons = new List<string>();

        var score = RiskCalculator.CheckTimingSignals(f, reasons, 0.0);

        Assert.Equal(0.2, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("no interaction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckTimingSignals_FirstInteractionUnder50ms_Adds0_15()
    {
        var f = MakeFeatures(sessionDurationMs: 5_000, timeToFirstInteractionMs: 20, clickCount: 1);
        var reasons = new List<string>();

        var score = RiskCalculator.CheckTimingSignals(f, reasons, 0.0);

        Assert.Equal(0.15, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("implausibly fast", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckTimingSignals_SessionUnder800msWithClick_Adds0_15()
    {
        var f = MakeFeatures(sessionDurationMs: 300, timeToFirstInteractionMs: 200, clickCount: 1);
        var reasons = new List<string>();

        var score = RiskCalculator.CheckTimingSignals(f, reasons, 0.0);

        Assert.Equal(0.15, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("completed in", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckTimingSignals_NormalPacing_AddsNothing()
    {
        var f = MakeFeatures(sessionDurationMs: 20_000, timeToFirstInteractionMs: 900, clickCount: 1);
        var reasons = new List<string>();

        var score = RiskCalculator.CheckTimingSignals(f, reasons, 0.0);

        Assert.Equal(0.0, score, precision: 3);
        Assert.Empty(reasons);
    }

    // --- Pointer (mouse + touch) -----------------------------------------

    [Fact]
    public void CheckPointerActivity_NoMouseAndNoTouch_Adds0_2()
    {
        var f = MakeFeatures(mouse: new PointerFeatures { SampleCount = 0 },
                             touch: new PointerFeatures { SampleCount = 0 });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckPointerActivity(f, reasons, 0.0);

        Assert.Equal(0.2, score, precision: 3);
        Assert.Single(reasons);
    }

    [Fact]
    public void CheckPointerActivity_TouchOnlyDevice_IsNotPenalised()
    {
        // Regression guard: a phone emits no mousemove at all. Charging the
        // "no pointer input" penalty for that flagged every mobile user.
        var f = MakeFeatures(
            mouse: new PointerFeatures { SampleCount = 0 },
            touch: new PointerFeatures
            {
                SampleCount = 120,
                StraightLineRatio = 0.1,
                DirectionChangeRate = 0.3,
                VelocityCv = 0.6,
            });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckPointerActivity(f, reasons, 0.0);

        Assert.Equal(0.0, score, precision: 3);
        Assert.Empty(reasons);
    }

    [Fact]
    public void CheckPointerActivity_SyntheticTouchPath_IsStillCaught()
    {
        // Touch is not a free pass: a generated touch path is judged exactly
        // like a generated mouse path.
        var f = MakeFeatures(
            mouse: new PointerFeatures { SampleCount = 0 },
            touch: new PointerFeatures
            {
                SampleCount = 50,
                StraightLineRatio = 0.95,
                DirectionChangeRate = 0.5,
                VelocityCv = 0.6,
            });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckPointerActivity(f, reasons, 0.0);

        Assert.Equal(0.25, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("touch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckPointerActivity_HighStraightLineRatio_Adds0_25()
    {
        var f = MakeFeatures(mouse: new PointerFeatures
        {
            SampleCount = 50,
            StraightLineRatio = 0.95,   // >= 0.85 threshold
            DirectionChangeRate = 0.5,  // above human floor, doesn't also fire
            VelocityCv = 0.6,           // above CV floor, doesn't also fire
        });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckPointerActivity(f, reasons, 0.0);

        Assert.Equal(0.25, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("straight-line", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckPointerActivity_LowDirectionChangeRate_Adds0_15()
    {
        var f = MakeFeatures(mouse: new PointerFeatures
        {
            SampleCount = 50,
            StraightLineRatio = 0.1,
            DirectionChangeRate = 0.01, // < 0.05 human floor
            VelocityCv = 0.6,
        });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckPointerActivity(f, reasons, 0.0);

        Assert.Equal(0.15, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("direction variance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckPointerActivity_LowVelocityCv_Adds0_1()
    {
        var f = MakeFeatures(mouse: new PointerFeatures
        {
            SampleCount = 50,
            StraightLineRatio = 0.1,
            DirectionChangeRate = 0.5,
            VelocityCv = 0.02,          // < 0.15 CV floor: near-constant speed
        });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckPointerActivity(f, reasons, 0.0);

        Assert.Equal(0.1, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("suspiciously constant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckPointerActivity_NaturalMouseMovement_AddsNothing()
    {
        var f = MakeFeatures(mouse: new PointerFeatures
        {
            SampleCount = 200,
            StraightLineRatio = 0.1,
            DirectionChangeRate = 0.3,
            VelocityCv = 0.8,
        });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckPointerActivity(f, reasons, 0.0);

        Assert.Equal(0.0, score, precision: 3);
        Assert.Empty(reasons);
    }

    // --- Keyboard ----------------------------------------------------------

    [Fact]
    public void CheckKeyboardActivity_UniformDwellTime_Adds0_15()
    {
        var f = MakeFeatures(keyboard: new KeyboardFeatures
        {
            SampleCount = 10,
            DwellCv = 0.02,   // < 0.15 CV floor
            FlightCv = 0.6,   // above the floor, doesn't also fire
        });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckKeyboardActivity(f, reasons, 0.0);

        Assert.Equal(0.15, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("dwell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckKeyboardActivity_UniformFlightTime_Adds0_1()
    {
        var f = MakeFeatures(keyboard: new KeyboardFeatures
        {
            SampleCount = 10,
            DwellCv = 0.6,
            FlightCv = 0.02,  // < 0.15 CV floor
        });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckKeyboardActivity(f, reasons, 0.0);

        Assert.Equal(0.1, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("flight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckKeyboardActivity_NoKeystrokes_AddsNothing()
    {
        // Zero variance on zero samples must NOT fire: the SampleCount > 3
        // guard is what protects a session that legitimately never typed,
        // now that there is no "insufficientData" escape hatch to rely on.
        var f = MakeFeatures(keyboard: new KeyboardFeatures
        {
            SampleCount = 0,
            DwellCv = 0.0,
            FlightCv = 0.0,
        });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckKeyboardActivity(f, reasons, 0.0);

        Assert.Equal(0.0, score, precision: 3);
        Assert.Empty(reasons);
    }

    [Fact]
    public void CheckKeyboardActivity_NaturalVariance_AddsNothing()
    {
        var f = MakeFeatures(keyboard: new KeyboardFeatures
        {
            SampleCount = 10,
            DwellCv = 0.5,
            FlightCv = 0.7,
        });
        var reasons = new List<string>();

        var score = RiskCalculator.CheckKeyboardActivity(f, reasons, 0.0);

        Assert.Equal(0.0, score, precision: 3);
        Assert.Empty(reasons);
    }

    // --- Scroll ------------------------------------------------------------

    [Fact]
    public void ScrollActivity_UniformDeltas_Adds0_05()
    {
        var f = MakeFeatures(scroll: new ScrollFeatures { SampleCount = 8, DeltaCv = 0.02 });
        var reasons = new List<string>();

        var score = RiskCalculator.ScrollActivity(f, reasons, 0.0);

        Assert.Equal(0.05, score, precision: 3);
        Assert.Contains(reasons, r => r.Contains("scroll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScrollActivity_NoScrolling_AddsNothing()
    {
        // Same protection as keyboard: SampleCount > 3 is the real gate.
        var f = MakeFeatures(scroll: new ScrollFeatures { SampleCount = 0, DeltaCv = 0.0 });
        var reasons = new List<string>();

        var score = RiskCalculator.ScrollActivity(f, reasons, 0.0);

        Assert.Equal(0.0, score, precision: 3);
        Assert.Empty(reasons);
    }

    // --- Test data builder ---------------------------------------------

    /// <summary>
    /// Builds a BotFeatures vector with sensible "clean" defaults for every
    /// field, letting each test override only the signal it's exercising.
    /// Every sub-object is populated, since they are all required now.
    /// </summary>
    private static BotFeatures MakeFeatures(
        double sessionDurationMs = 20_000,
        double? timeToFirstInteractionMs = 900,
        int clickCount = 1,
        EnvironmentFeatures? env = null,
        PointerFeatures? mouse = null,
        PointerFeatures? touch = null,
        KeyboardFeatures? keyboard = null,
        ScrollFeatures? scroll = null)
    {
        return new BotFeatures
        {
            SessionDurationMs = sessionDurationMs,
            TimeToFirstInteractionMs = timeToFirstInteractionMs,
            ClickCount = clickCount,
            FocusOrderLength = 1,
            Environment = env ?? new EnvironmentFeatures { Webdriver = false, LanguagesCount = 2, HasPlugins = true },
            Mouse = mouse ?? new PointerFeatures { SampleCount = 100, StraightLineRatio = 0.1, DirectionChangeRate = 0.3, VelocityCv = 0.8 },
            Touch = touch ?? new PointerFeatures { SampleCount = 0 },
            Keyboard = keyboard ?? new KeyboardFeatures { SampleCount = 10, DwellCv = 0.5, FlightCv = 0.7 },
            Scroll = scroll ?? new ScrollFeatures { SampleCount = 8, DeltaCv = 0.5 },
        };
    }
}