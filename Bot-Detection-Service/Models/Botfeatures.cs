namespace BotDetection;

/// <summary>
/// The feature vector the caller submits for scoring.
///
/// Every sub-object is REQUIRED and seeded with `null!` rather than `new()`:
/// a `new()` default would silently substitute an empty vector for an omitted
/// field, reproducing the "absent data scores as zero risk" hole. With
/// `null!` an omitted or explicitly-null field binds null and
/// ScoreRequestValidator rejects the request with a 400.
///
/// All of this is browser-reported and therefore forgeable. It is
/// corroborating evidence that catches lazy automation — never proof, and it
/// must never be able to LOWER a score.
/// </summary>
public sealed class BotFeatures
{
    public double SessionDurationMs { get; init; }
    public double? TimeToFirstInteractionMs { get; init; }

    public EnvironmentFeatures Environment { get; init; } = null!;

    /// <summary>Mouse movement. Empty on touch-only devices — see Touch.</summary>
    public PointerFeatures Mouse { get; init; } = null!;

    /// <summary>
    /// Touch movement, analysed identically to the mouse. Present so that
    /// phone and tablet users are not penalised for the absence of a mouse
    /// they do not have. Deliberately measured as real samples rather than
    /// trusting the Environment.HasTouch flag: a flag is a self-declared
    /// opt-out a scraper could simply assert, whereas faking this requires
    /// fabricating touch telemetry that is then scrutinised like any other.
    /// </summary>
    public PointerFeatures Touch { get; init; } = null!;

    public KeyboardFeatures Keyboard { get; init; } = null!;
    public ScrollFeatures Scroll { get; init; } = null!;
    public int ClickCount { get; init; }
    public int FocusOrderLength { get; init; }
}

/// <summary>
/// Movement statistics for a stream of pointer positions. Used for both
/// mouse and touch: synthetic touch is as unnaturally linear and
/// evenly-paced as synthetic mouse movement, so both are judged the same way.
/// </summary>
public sealed class PointerFeatures
{
    public int SampleCount { get; init; }

    /// <summary>Informational only (px/ms); not scored, since its scale rides on DPI.</summary>
    public double MeanVelocity { get; init; }

    /// <summary>
    /// Coefficient of variation (stddev / mean) of velocity. Dimensionless,
    /// so the threshold means the same thing on a 4K desktop and a phone —
    /// unlike raw variance in px/ms, whose scale depends on resolution.
    /// </summary>
    public double VelocityCv { get; init; }

    public double DirectionChangeRate { get; init; }
    public double StraightLineRatio { get; init; }
}

public sealed class KeyboardFeatures
{
    /// <summary>Number of completed key presses (keydown paired with its keyup).</summary>
    public int SampleCount { get; init; }

    /// <summary>How long a key was held: keydown -> its own keyup.</summary>
    public double MeanDwellMs { get; init; }
    public double DwellCv { get; init; }

    /// <summary>Gap between releasing one key and pressing the next: keyup -> next keydown.</summary>
    public double MeanFlightMs { get; init; }
    public double FlightCv { get; init; }
}

public sealed class ScrollFeatures
{
    public int SampleCount { get; init; }
    public double MeanDelta { get; init; }
    public double DeltaCv { get; init; }
}

public sealed class EnvironmentFeatures
{
    public bool Webdriver { get; init; }
    public int LanguagesCount { get; init; }
    public int? HardwareConcurrency { get; init; }
    public double? DeviceMemory { get; init; }

    /// <summary>
    /// Informational only, NOT scored: an empty plugin list is normal in
    /// privacy-hardened browsers (and with various extensions), so scoring it
    /// punished legitimate users.
    /// </summary>
    public bool? HasPlugins { get; init; }

    /// <summary>
    /// Informational only, NOT scored: touch CAPABILITY is a self-declared
    /// boolean a scraper could assert to dodge the pointer check. Actual
    /// touch usage is measured via BotFeatures.Touch instead.
    /// </summary>
    public bool HasTouch { get; init; }
    public int? MaxTouchPoints { get; init; }

    public int? ScreenW { get; init; }
    public int? ScreenH { get; init; }
    public int InnerW { get; init; }
    public int InnerH { get; init; }
    public int TimezoneOffset { get; init; }
}