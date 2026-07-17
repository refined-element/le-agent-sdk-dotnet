namespace LightningEnable.AgentSdk.Models;

/// <summary>
/// Aggregated reputation score for an agent.
/// </summary>
public class ReputationScore
{
    /// <summary>Lowest rating an attestation may carry.</summary>
    public const int MinRating = 1;

    /// <summary>Highest rating an attestation may carry.</summary>
    public const int MaxRating = 5;

    public string Pubkey { get; set; } = string.Empty;
    public double AverageRating { get; set; }
    public int TotalAttestations { get; set; }
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public int NeutralCount { get; set; }

    /// <summary>
    /// Aggregate attestations into a reputation score.
    /// </summary>
    /// <remarks>
    /// Only ratings within the valid 1-5 range are counted. Publishing enforces
    /// that range locally, but nothing stops a hostile agent from putting any
    /// integer on the wire, and a single out-of-range rating would otherwise skew
    /// the average arbitrarily.
    /// </remarks>
    public static ReputationScore FromAttestations(string pubkey, IEnumerable<AgentAttestation> attestations)
    {
        var list = attestations
            .Where(a => a.Rating >= MinRating && a.Rating <= MaxRating)
            .ToList();
        var score = new ReputationScore
        {
            Pubkey = pubkey,
            TotalAttestations = list.Count,
            PositiveCount = list.Count(a => a.Rating > 3),
            NegativeCount = list.Count(a => a.Rating < 3),
            NeutralCount = list.Count(a => a.Rating == 3),
            AverageRating = list.Count > 0 ? list.Average(a => a.Rating) : 0
        };
        return score;
    }
}
