using Bot_Detection_Service.Models;
using Bot_Detection_Service.Services;

namespace Bot_Detection.Tests
{
    public class BotScorerTests
    {
        /// <summary>
        /// This test covers the clearest case: a session with the
        /// navigator.webdriver flag set and no mouse movement at all — the
        /// signature of an unmodified Selenium/Puppeteer bot filling a form
        /// programmatically. It should score decisively as a bot.
        /// </summary>
        [Fact]
        public void ObviousBot_WebdriverFlagAndNoMouseMovement_ScoresAsBot()
        {
            var features = new BotFeatures
            {
                SessionDurationMs = 150,
                TimeToFirstInteractionMs = null,
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

            var scorer = new BotScorer();
            var result = scorer.Score(features);

            Assert.Equal(Verdict.Bot, result.Verdict);
            Assert.True(result.Score >= 0.6, $"Expected score >= 0.6, got {result.Score}");
            Assert.Contains(result.Reasons, r => r.Contains("webdriver", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Reasons, r => r.Contains("mouse", StringComparison.OrdinalIgnoreCase));
        }
    }
}
