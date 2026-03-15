namespace LightningEnable.AgentSdk.Agent;

/// <summary>
/// Configuration options for the AgentManager.
/// </summary>
public class AgentManagerOptions
{
    /// <summary>
    /// Hex-encoded Nostr private key for signing events.
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// List of relay URLs to connect to.
    /// </summary>
    public List<string> RelayUrls { get; set; } = new() { "wss://relay.damus.io" };

    /// <summary>
    /// Lightning Enable API base URL for L402 operations.
    /// </summary>
    public string LightningEnableApiUrl { get; set; } = "https://api.lightningenable.com";

    /// <summary>
    /// API key for Lightning Enable (producer operations).
    /// </summary>
    public string? LightningEnableApiKey { get; set; }

    /// <summary>
    /// Optional HttpClient for custom HTTP configuration.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}

/// <summary>
/// Options for capability discovery.
/// </summary>
public class DiscoverOptions
{
    /// <summary>
    /// Filter by category tag.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Filter by specific author pubkey.
    /// </summary>
    public string? AuthorPubkey { get; set; }

    /// <summary>
    /// Maximum number of results.
    /// </summary>
    public int Limit { get; set; } = 50;

    /// <summary>
    /// Only return capabilities created after this timestamp.
    /// </summary>
    public long? Since { get; set; }
}
