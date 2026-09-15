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
}
