using Bot_Detection_Service.Services;

namespace Bot_Detection.Tests
{
    public class BotScorerVerdictTests
    {
        [Theory]
        [InlineData(0.0, Verdict.Human)]
        [InlineData(0.15, Verdict.Human)]
        [InlineData(0.29, Verdict.Human)]
        [InlineData(0.2999, Verdict.Human)]
        [InlineData(0.3, Verdict.Suspicious)]
        [InlineData(0.45, Verdict.Suspicious)]
        [InlineData(0.59, Verdict.Suspicious)]
        [InlineData(0.5999, Verdict.Suspicious)]
        [InlineData(0.6, Verdict.Bot)]
        [InlineData(0.75, Verdict.Bot)]
        [InlineData(1.0, Verdict.Bot)]
        public void DetermineVerdict_AtAndAroundThresholds_ReturnsExpectedBand(double score, Verdict expected)
        {
            var actual = BotScorer.DetermineVerdict(score);

            Assert.Equal(expected, actual);
        }
    }
}
