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

    // --- Out-of-range ratings ---

    private static AgentAttestation Rated(int rating) => new()
    {
        SubjectPubkey = "subject",
        AgreementId = "agreement",
        Rating = rating
    };

    [Fact]
    public void FromAttestations_IgnoresRatingsAboveTheValidRange()
    {
        // PublishAttestationAsync enforces 1-5 locally, but nothing stops a hostile
        // agent from putting any integer on the wire. One rating=999999 must not
        // skew the average.
        var score = ReputationScore.FromAttestations("subject", new[]
        {
            Rated(5),
            Rated(999999)
        });

        Assert.Equal(5, score.AverageRating);
        Assert.Equal(1, score.TotalAttestations);
        Assert.Equal(1, score.PositiveCount);
    }

    [Fact]
    public void FromAttestations_IgnoresRatingsBelowTheValidRange()
    {
        var score = ReputationScore.FromAttestations("subject", new[]
        {
            Rated(4),
            Rated(0),
            Rated(-100)
        });

        Assert.Equal(4, score.AverageRating);
        Assert.Equal(1, score.TotalAttestations);
        Assert.Equal(0, score.NegativeCount);
    }

    [Fact]
    public void FromAttestations_ReturnsZeroWhenEveryRatingIsOutOfRange()
    {
        var score = ReputationScore.FromAttestations("subject", new[]
        {
            Rated(999999),
            Rated(-1)
        });

        Assert.Equal(0, score.AverageRating);
        Assert.Equal(0, score.TotalAttestations);
    }

    [Fact]
    public void FromAttestations_AveragesInRangeRatings()
    {
        var score = ReputationScore.FromAttestations("subject", new[]
        {
            Rated(5),
            Rated(3),
            Rated(1)
        });

        Assert.Equal(3, score.AverageRating);
        Assert.Equal(3, score.TotalAttestations);
        Assert.Equal(1, score.PositiveCount);
        Assert.Equal(1, score.NeutralCount);
        Assert.Equal(1, score.NegativeCount);
    }

    [Fact]
    public void FromAttestations_HandlesNoAttestations()
    {
        var score = ReputationScore.FromAttestations("subject", Array.Empty<AgentAttestation>());

        Assert.Equal(0, score.AverageRating);
        Assert.Equal(0, score.TotalAttestations);
        Assert.Equal("subject", score.Pubkey);
    }
}
