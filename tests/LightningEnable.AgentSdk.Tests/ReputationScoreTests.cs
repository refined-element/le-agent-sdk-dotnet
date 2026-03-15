using LightningEnable.AgentSdk.Models;

namespace LightningEnable.AgentSdk.Tests;

public class ReputationScoreTests
{
    [Fact]
    public void FromAttestations_CalculatesCorrectly()
    {
        var attestations = new List<AgentAttestation>
        {
            new() { Rating = 5 },
            new() { Rating = 4 },
            new() { Rating = 3 },
            new() { Rating = 2 },
            new() { Rating = 5 }
        };

        var score = ReputationScore.FromAttestations("test-pub", attestations);

        Assert.Equal("test-pub", score.Pubkey);
        Assert.Equal(5, score.TotalAttestations);
        Assert.Equal(3, score.PositiveCount);  // ratings > 3: 5, 4, 5
        Assert.Equal(1, score.NegativeCount);  // ratings < 3: 2
        Assert.Equal(1, score.NeutralCount);   // ratings == 3: 3
        Assert.Equal(3.8, score.AverageRating, 1);
    }

    [Fact]
    public void FromAttestations_EmptyList_ReturnsZero()
    {
        var score = ReputationScore.FromAttestations("empty-pub", new List<AgentAttestation>());

        Assert.Equal(0, score.TotalAttestations);
        Assert.Equal(0, score.AverageRating);
    }
}
