using Bot_Detection_Service.Services;

namespace Bot_Detection.Tests
{
    public class BotScorerActionTests
    {
        [Theory]
        [InlineData(0.0, RiskAction.Allow)]
        [InlineData(0.15, RiskAction.Allow)]
        [InlineData(0.29, RiskAction.Allow)]
        [InlineData(0.2999, RiskAction.Allow)]
        [InlineData(0.3, RiskAction.Challenge)]
        [InlineData(0.45, RiskAction.Challenge)]
        [InlineData(0.59, RiskAction.Challenge)]
        [InlineData(0.5999, RiskAction.Challenge)]
        [InlineData(0.6, RiskAction.Block)]
        [InlineData(0.75, RiskAction.Block)]
        [InlineData(1.0, RiskAction.Block)]
        public void DetermineVerdict_AtAndAroundThresholds_ReturnsExpectedBand(double score, RiskAction expected)
        {
            var actual = RiskCalculator.DetermineAction(score);

            Assert.Equal(expected, actual);
        }
    }
}
