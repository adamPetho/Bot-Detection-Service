using Bot_Detection_Service.Models;
using Bot_Detection_Service.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bot_Detection.Tests
{
    public class BotScorerIntegrationTests
    {
        private readonly BotScorer _scorer = new();

        [Fact]
        public void ObviousBot_WebdriverFlagAndNoMouseMovement_ScoresAsBot()
        {
            var features = new BotFeatures
            {
                SessionDurationMs = 150,
                TimeToFirstInteractionMs = null, // no interaction recorded before "submit"
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
                Mouse = new MouseFeatures { SampleCount = 0, InsufficientData = true },
                Keyboard = new KeyboardFeatures { SampleCount = 0, InsufficientData = true },
                Scroll = new ScrollFeatures { SampleCount = 0, InsufficientData = true },
            };

            var result = _scorer.Score(features);

            Assert.Equal(Verdict.Bot, result.Verdict);
            Assert.True(result.Score >= 0.6, $"Expected score >= 0.6, got {result.Score}");
            Assert.Contains(result.Reasons, r => r.Contains("webdriver", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Reasons, r => r.Contains("mouse", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CleanHumanSession_AllNaturalSignals_ScoresAsHuman()
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
                Mouse = new MouseFeatures
                {
                    SampleCount = 240,
                    InsufficientData = false,
                    MeanVelocity = 0.8,
                    VelocityVariance = 1.4,      // well above the 0.5 "suspiciously constant" floor
                    DirectionChangeRate = 0.22,  // well above the 0.05 human floor
                    StraightLineRatio = 0.10,    // well below the 0.85 bot threshold
                },
                Keyboard = new KeyboardFeatures
                {
                    SampleCount = 18,
                    InsufficientData = false,
                    MeanDwellMs = 95,
                    DwellVariance = 12.3,
                    MeanFlightMs = 140,
                    FlightVariance = 20.7,
                },
                Scroll = new ScrollFeatures
                {
                    SampleCount = 9,
                    InsufficientData = false,
                    MeanDelta = 80,
                    DeltaVariance = 15.6,
                },
            };

            var result = _scorer.Score(features);

            Assert.Equal(Verdict.Human, result.Verdict);
            Assert.True(result.Score < 0.3, $"Expected score < 0.3, got {result.Score}");
            Assert.Empty(result.Reasons);
        }

        [Fact]
        public void MixedSignals_NoMouseDataPlusFastInteraction_ScoresAsSuspicious()
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
                Mouse = new MouseFeatures { SampleCount = 0, InsufficientData = true },
                Keyboard = new KeyboardFeatures { SampleCount = 0, InsufficientData = true },
                Scroll = new ScrollFeatures { SampleCount = 0, InsufficientData = true },
            };

            var result = _scorer.Score(features);

            Assert.Equal(Verdict.Suspicious, result.Verdict);
            Assert.Equal(0.35, result.Score, precision: 3);
            Assert.Equal(2, result.Reasons.Count);
        }

        [Fact]
        public void Real_Human_Test_Values()
        {
            var features = new BotFeatures
            {
                SessionDurationMs = 21_460,
                TimeToFirstInteractionMs = 779,
                ClickCount = 2,
                FocusOrderLength = 3,
                Environment = new EnvironmentFeatures
                {
                    Webdriver = false,
                    LanguagesCount = 2,
                    HardwareConcurrency = 20,
                    DeviceMemory = 16,
                    HasPlugins = true,
                    HasTouch = false,
                    ScreenW = 1536,
                    ScreenH = 864,
                    InnerW = 1526,
                    InnerH = 696,
                    TimezoneOffset = -120
                },
                Mouse = new MouseFeatures
                {
                    SampleCount = 383,
                    InsufficientData = false,
                    MeanVelocity = 0.576,
                    VelocityVariance = 0.26,
                    DirectionChangeRate = 0.52,
                    StraightLineRatio = 0.283
                },
                Keyboard = new KeyboardFeatures
                {
                    SampleCount = 39,
                    InsufficientData = false,
                    MeanDwellMs = 111.59,
                    DwellVariance = 5880.633,
                    MeanFlightMs = 264.83,
                    FlightVariance = 232848.67,
                },
                Scroll = new ScrollFeatures
                {
                    SampleCount = 269,
                    InsufficientData = false,
                    MeanDelta = 3.4026,
                    DeltaVariance = 4.73,
                },
            };

            var result = _scorer.Score(features);

            Assert.Equal(Verdict.Human, result.Verdict);
            Assert.True(result.Score < 0.3, $"Expected score < 0.3, got {result.Score}");
            Assert.Contains(result.Reasons, r => r.Contains("mouse velocity", StringComparison.OrdinalIgnoreCase));
        }
    }
}
