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
    /// List of relay URLs to connect to. Defaults to the Lightning Enable agent
    /// relay, which carries the ASA event kinds (38400-38403) this SDK publishes
    /// and reads. General-purpose public relays are not part of the agent network
    /// and drop these kinds, so an agent pointed at one would publish into a void
    /// and discover nothing.
    /// </summary>
    public List<string> RelayUrls { get; set; } = new() { "wss://agents.lightningenable.com" };

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

    /// <summary>
    /// Pays a BOLT-11 invoice and returns the hex-encoded preimage as proof.
    /// Required by <see cref="AgentManager.SettleAsync"/> to settle an L402
    /// challenge. Without it, settlement cannot complete a paid request.
    /// </summary>
    public Func<string, CancellationToken, Task<string>>? PayInvoiceCallback { get; set; }

    /// <summary>
    /// Maximum amount, in satoshis, that <see cref="AgentManager.SettleAsync"/> may
    /// pay for a single settlement.
    /// </summary>
    /// <remarks>
    /// When set, an invoice whose amount cannot be determined is refused rather
    /// than paid: an unknown amount must never be read as "no limit applies".
    /// When null there is no ceiling and the callback is trusted to impose its
    /// own limits.
    /// </remarks>
    public int? MaxAmountSats { get; set; }
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
