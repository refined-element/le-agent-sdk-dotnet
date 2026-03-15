namespace LightningEnable.AgentSdk.L402;

/// <summary>
/// Represents an L402 challenge issued by a producer.
/// </summary>
public class L402ChallengeResponse
{
    public string Macaroon { get; set; } = string.Empty;
    public string Invoice { get; set; } = string.Empty;
    public string PaymentHash { get; set; } = string.Empty;
    public int PriceSats { get; set; }
    public string Description { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
}
