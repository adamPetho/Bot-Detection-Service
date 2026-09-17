using Bot_Detection_Service.Models;
using Bot_Detection_Service.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bot_Detection.Tests
{
    public class BotScorerSignalTests
    {
        // --- Environment --------------------------------------------------

        [Fact]
        public void CheckEnvironmentSignals_WebdriverFlagSet_Adds0_5AndReason()
        {
            var f = MakeFeatures(env: new EnvironmentFeatures { Webdriver = true, LanguagesCount = 1, HasPlugins = true });
            var reasons = new List<string>();

            var score = BotScorer.CheckEnvironmentSignals(f, reasons, 0.0);

            Assert.Equal(0.5, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("webdriver", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckEnvironmentSignals_NoLanguagesReported_Adds0_05()
        {
            var f = MakeFeatures(env: new EnvironmentFeatures { Webdriver = false, LanguagesCount = 0, HasPlugins = true });
            var reasons = new List<string>();

            var score = BotScorer.CheckEnvironmentSignals(f, reasons, 0.0);

            Assert.Equal(0.05, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("languages", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckEnvironmentSignals_NoPluginsReported_Adds0_03()
        {
            var f = MakeFeatures(env: new EnvironmentFeatures { Webdriver = false, LanguagesCount = 1, HasPlugins = false });
            var reasons = new List<string>();

            var score = BotScorer.CheckEnvironmentSignals(f, reasons, 0.0);

            Assert.Equal(0.03, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("plugins", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckEnvironmentSignals_AllClean_AddsNothing()
        {
            var f = MakeFeatures(env: new EnvironmentFeatures { Webdriver = false, LanguagesCount = 2, HasPlugins = true });
            var reasons = new List<string>();

            var score = BotScorer.CheckEnvironmentSignals(f, reasons, 0.0);

            Assert.Equal(0.0, score, precision: 3);
            Assert.Empty(reasons);
        }

        // --- Timing --------------------------------------------------------

        [Fact]
        public void CheckTimingSignals_NoInteractionRecorded_Adds0_2()
        {
            var f = MakeFeatures(sessionDurationMs: 5_000, timeToFirstInteractionMs: null, clickCount: 1);
            var reasons = new List<string>();

            var score = BotScorer.CheckTimingSignals(f, reasons, 0.0);

            Assert.Equal(0.2, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("no interaction", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckTimingSignals_FirstInteractionUnder50ms_Adds0_15()
        {
            var f = MakeFeatures(sessionDurationMs: 5_000, timeToFirstInteractionMs: 20, clickCount: 1);
            var reasons = new List<string>();

            var score = BotScorer.CheckTimingSignals(f, reasons, 0.0);

            Assert.Equal(0.15, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("implausibly fast", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckTimingSignals_SessionUnder800msWithClick_Adds0_15()
        {
            var f = MakeFeatures(sessionDurationMs: 300, timeToFirstInteractionMs: 200, clickCount: 1);
            var reasons = new List<string>();

            var score = BotScorer.CheckTimingSignals(f, reasons, 0.0);

            Assert.Equal(0.15, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("completed in", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckTimingSignals_NormalPacing_AddsNothing()
        {
            var f = MakeFeatures(sessionDurationMs: 20_000, timeToFirstInteractionMs: 900, clickCount: 1);
            var reasons = new List<string>();

            var score = BotScorer.CheckTimingSignals(f, reasons, 0.0);

            Assert.Equal(0.0, score, precision: 3);
            Assert.Empty(reasons);
        }

        // --- Mouse -----------------------------------------------------------

        [Fact]
        public void CheckMouseActivity_InsufficientData_Adds0_2AndSkipsFurtherChecks()
        {
            var f = MakeFeatures(mouse: new MouseFeatures { SampleCount = 0, InsufficientData = true });
            var reasons = new List<string>();

            var score = BotScorer.CheckMouseActivity(f, reasons, 0.0);

            Assert.Equal(0.2, score, precision: 3);
            Assert.Single(reasons);
        }

        [Fact]
        public void CheckMouseActivity_HighStraightLineRatio_Adds0_25()
        {
            var f = MakeFeatures(mouse: new MouseFeatures
            {
                SampleCount = 50,
                InsufficientData = false,
                StraightLineRatio = 0.95,   // >= 0.85 threshold
                DirectionChangeRate = 0.5,  // above human floor, doesn't also fire
                VelocityVariance = 5.0,     // above epsilon, doesn't also fire
            });
            var reasons = new List<string>();

            var score = BotScorer.CheckMouseActivity(f, reasons, 0.0);

            Assert.Equal(0.25, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("straight-line", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckMouseActivity_LowDirectionChangeRate_Adds0_15()
        {
            var f = MakeFeatures(mouse: new MouseFeatures
            {
                SampleCount = 50,
                InsufficientData = false,
                StraightLineRatio = 0.1,    // below threshold, doesn't fire
                DirectionChangeRate = 0.01, // < 0.05 human floor
                VelocityVariance = 5.0,     // above epsilon, doesn't fire
            });
            var reasons = new List<string>();

            var score = BotScorer.CheckMouseActivity(f, reasons, 0.0);

            Assert.Equal(0.15, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("direction variance", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckMouseActivity_LowVelocityVariance_Adds0_1()
        {
            var f = MakeFeatures(mouse: new MouseFeatures
            {
                SampleCount = 50,
                InsufficientData = false,
                StraightLineRatio = 0.1,
                DirectionChangeRate = 0.5,
                VelocityVariance = 0.1,     // < 0.5 epsilon
            });
            var reasons = new List<string>();

            var score = BotScorer.CheckMouseActivity(f, reasons, 0.0);

            Assert.Equal(0.1, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("suspiciously constant", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckMouseActivity_NaturalMovement_AddsNothing()
        {
            var f = MakeFeatures(mouse: new MouseFeatures
            {
                SampleCount = 200,
                InsufficientData = false,
                StraightLineRatio = 0.1,
                DirectionChangeRate = 0.3,
                VelocityVariance = 2.0,
            });
            var reasons = new List<string>();

            var score = BotScorer.CheckMouseActivity(f, reasons, 0.0);

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
                InsufficientData = false,
                DwellVariance = 0.1,   // < 0.5 epsilon
                FlightVariance = 5.0,  // above epsilon, doesn't also fire
            });
            var reasons = new List<string>();

            var score = BotScorer.CheckKeyboardActivity(f, reasons, 0.0);

            Assert.Equal(0.15, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("dwell", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckKeyboardActivity_UniformFlightTime_Adds0_1()
        {
            var f = MakeFeatures(keyboard: new KeyboardFeatures
            {
                SampleCount = 10,
                InsufficientData = false,
                DwellVariance = 5.0,   // above epsilon, doesn't fire
                FlightVariance = 0.1,  // < 0.5 epsilon
            });
            var reasons = new List<string>();

            var score = BotScorer.CheckKeyboardActivity(f, reasons, 0.0);

            Assert.Equal(0.1, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("flight", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CheckKeyboardActivity_InsufficientData_SkipsEntirely()
        {
            // Uniform variance values that WOULD fire if InsufficientData were
            // false — proving the early-return guard actually short-circuits.
            var f = MakeFeatures(keyboard: new KeyboardFeatures
            {
                SampleCount = 0,
                InsufficientData = true,
                DwellVariance = 0.0,
                FlightVariance = 0.0,
            });
            var reasons = new List<string>();

            var score = BotScorer.CheckKeyboardActivity(f, reasons, 0.0);

            Assert.Equal(0.0, score, precision: 3);
            Assert.Empty(reasons);
        }

        [Fact]
        public void CheckKeyboardActivity_NaturalVariance_AddsNothing()
        {
            var f = MakeFeatures(keyboard: new KeyboardFeatures
            {
                SampleCount = 10,
                InsufficientData = false,
                DwellVariance = 10.0,
                FlightVariance = 15.0,
            });
            var reasons = new List<string>();

            var score = BotScorer.CheckKeyboardActivity(f, reasons, 0.0);

            Assert.Equal(0.0, score, precision: 3);
            Assert.Empty(reasons);
        }

        // --- Scroll ------------------------------------------------------------

        [Fact]
        public void ScrollActivity_UniformDeltas_Adds0_05()
        {
            var f = MakeFeatures(scroll: new ScrollFeatures
            {
                SampleCount = 8,
                InsufficientData = false,
                DeltaVariance = 0.1, // < 0.5 epsilon
            });
            var reasons = new List<string>();

            var score = BotScorer.ScrollActivity(f, reasons, 0.0);

            Assert.Equal(0.05, score, precision: 3);
            Assert.Contains(reasons, r => r.Contains("scroll", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ScrollActivity_InsufficientData_AddsNothing()
        {
            var f = MakeFeatures(scroll: new ScrollFeatures
            {
                SampleCount = 0,
                InsufficientData = true,
                DeltaVariance = 0.0, // would fire if InsufficientData were false
            });
            var reasons = new List<string>();

            var score = BotScorer.ScrollActivity(f, reasons, 0.0);

            Assert.Equal(0.0, score, precision: 3);
            Assert.Empty(reasons);
        }

        // --- Test data builder ---------------------------------------------

        /// <summary>
        /// Builds a BotFeatures vector with sensible "clean" defaults for every
        /// field, letting each test override only the signal it's exercising.
        /// Keeps tests focused on the one thing they're checking instead of
        /// repeating a full object literal every time.
        /// </summary>
        private static BotFeatures MakeFeatures(
            double sessionDurationMs = 20_000,
            double? timeToFirstInteractionMs = 900,
            int clickCount = 1,
            EnvironmentFeatures? env = null,
            MouseFeatures? mouse = null,
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
                Mouse = mouse ?? new MouseFeatures { SampleCount = 100, InsufficientData = false, StraightLineRatio = 0.1, DirectionChangeRate = 0.3, VelocityVariance = 2.0 },
                Keyboard = keyboard ?? new KeyboardFeatures { SampleCount = 10, InsufficientData = false, DwellVariance = 10.0, FlightVariance = 15.0 },
                Scroll = scroll ?? new ScrollFeatures { SampleCount = 8, InsufficientData = false, DeltaVariance = 10.0 },
            };
        }
    }
}
