namespace Bot_Detection_Service.Models
{
    /// <summary>
    /// Mirrors the JSON shape produced by telemetry-collector.js's computeFeatures().
    /// </summary>
    public sealed class BotFeatures
    {
        public double SessionDurationMs { get; init; }
        public double? TimeToFirstInteractionMs { get; init; }

        public EnvironmentFeatures Environment { get; init; } = new();
        public MouseFeatures Mouse { get; init; } = new();
        public KeyboardFeatures Keyboard { get; init; } = new();
        public ScrollFeatures Scroll { get; init; } = new();
        public RequestPatternFeatures RequestPattern { get; init; } = new();

        public int ClickCount { get; init; }
        public int FocusOrderLength { get; init; }
    }

    public sealed class MouseFeatures
    {
        public int SampleCount { get; init; }
        public bool InsufficientData { get; init; }
        public double MeanVelocity { get; init; }
        public double VelocityVariance { get; init; }
        public double DirectionChangeRate { get; init; }
        public double StraightLineRatio { get; init; }
    }

    public sealed class KeyboardFeatures
    {
        public int SampleCount { get; init; }
        public bool InsufficientData { get; init; }
        public double MeanDwellMs { get; init; }
        public double DwellVariance { get; init; }
        public double MeanFlightMs { get; init; }
        public double FlightVariance { get; init; }
    }

    public sealed class ScrollFeatures
    {
        public int SampleCount { get; init; }
        public bool InsufficientData { get; init; }
        public double MeanDelta { get; init; }
        public double DeltaVariance { get; init; }
    }

    public sealed class EnvironmentFeatures
    {
        public bool Webdriver { get; init; }
        public int LanguagesCount { get; init; }
        public int? HardwareConcurrency { get; init; }
        public double? DeviceMemory { get; init; }
        public bool? HasPlugins { get; init; }
        public bool HasTouch { get; init; }
        public int? ScreenW { get; init; }
        public int? ScreenH { get; init; }
        public int InnerW { get; init; }
        public int InnerH { get; init; }
        public int TimezoneOffset { get; init; }
    }

    public sealed class RequestPatternFeatures
    {
        public bool InsufficientData { get; init; }
        public double WindowMinutes { get; init; }
        public int RequestCount { get; init; }
        public int UniqueResourceCount { get; init; }
        public double RequestsPerUniqueResource { get; init; }
        public double MeanIntervalMs { get; init; }
        public double IntervalVarianceMs { get; init; }

        // Fraction of consecutive requests (ordered by time) whose resource ID
        // differs by exactly 1 from the previous one — an enumeration signature
        // ("/companies/1001", "/companies/1002", ...). Only meaningful for
        // resources identified by sequential/guessable IDs in your URLs or API
        // params; doesn't apply to the opaque signed download tokens themselves.
        public double SequentialIdRatio { get; init; }

        // Fraction of download requests that had a matching "view" request for
        // the same resource ID earlier in the window — i.e. did the user look
        // at the thing before downloading it, the way a real researcher does.
        public double BrowsingTrailRatio { get; init; }

        // True if any request in the window touched a decoy resource that is
        // reachable but never linked from the real UI — only discoverable by
        // something enumerating IDs or crawling systematically. Near-certain
        // signal on its own; see CheckRequestPatternSignals for why it's
        // checked even when InsufficientData is otherwise true.
        public bool HitHoneypot { get; init; }
    }
}
