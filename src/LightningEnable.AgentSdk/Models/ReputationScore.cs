namespace LightningEnable.AgentSdk.Models;

/// <summary>
/// Aggregated reputation score for an agent.
/// </summary>
public class ReputationScore
{
    public string Pubkey { get; set; } = string.Empty;
    public double AverageRating { get; set; }
    public int TotalAttestations { get; set; }
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public int NeutralCount { get; set; }

    public static ReputationScore FromAttestations(string pubkey, IEnumerable<AgentAttestation> attestations)
    {
        var list = attestations.ToList();
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
