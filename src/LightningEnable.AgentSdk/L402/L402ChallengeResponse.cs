namespace LightningEnable.AgentSdk.L402;

/// <summary>
/// Represents an L402 challenge issued by a producer.
/// When IsMpp is true, Macaroon may be null (MPP challenges carry only an invoice).
/// </summary>
public class L402ChallengeResponse
{
    /// <summary>
    /// The macaroon token. Null when the challenge uses MPP (Payment scheme) instead of L402.
    /// </summary>
    public string? Macaroon { get; set; } = string.Empty;

    public string Invoice { get; set; } = string.Empty;
    public string PaymentHash { get; set; } = string.Empty;
    public int PriceSats { get; set; }
    public string Description { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }

    /// <summary>
    /// True when this challenge was parsed from a Payment (MPP) WWW-Authenticate header
    /// rather than an L402 header. MPP challenges have no macaroon.
    /// </summary>
    public bool IsMpp { get; set; }
}
