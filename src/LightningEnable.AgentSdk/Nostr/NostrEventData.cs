namespace LightningEnable.AgentSdk.Nostr;

/// <summary>
/// Represents a complete Nostr event with all fields.
/// </summary>
public class NostrEventData
{
    public string Id { get; set; } = string.Empty;
    public string Pubkey { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public int Kind { get; set; }
    public string[][] Tags { get; set; } = Array.Empty<string[]>();
    public string Content { get; set; } = string.Empty;
    public string Sig { get; set; } = string.Empty;
}
