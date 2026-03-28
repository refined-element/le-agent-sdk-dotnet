using System.Text.Json;
using LightningEnable.AgentSdk.L402;
using LightningEnable.AgentSdk.Models;
using LightningEnable.AgentSdk.Nostr;

namespace LightningEnable.AgentSdk.Agent;

/// <summary>
/// Main entry point for the Lightning Enable Agent SDK.
/// Handles discovery, publishing, settlement, and attestations.
/// </summary>
public class AgentManager : IAsyncDisposable
{
    private readonly AgentManagerOptions _options;
    private readonly string _pubkey;
    private readonly List<NostrRelay> _relays = new();
    private readonly L402Client _l402Client;
    private readonly L402ProducerClient? _producerClient;
    private bool _connected;

    public AgentManager(AgentManagerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrEmpty(options.PrivateKey))
            throw new ArgumentException("PrivateKey is required", nameof(options));

        _pubkey = NostrEvent.GetPublicKey(options.PrivateKey);
        _l402Client = new L402Client(options.HttpClient);

        if (!string.IsNullOrEmpty(options.LightningEnableApiKey))
        {
            _producerClient = new L402ProducerClient(
                options.LightningEnableApiUrl,
                options.LightningEnableApiKey,
                options.HttpClient);
        }
    }

    /// <summary>
    /// The public key derived from the configured private key.
    /// </summary>
    public string Pubkey => _pubkey;

    /// <summary>
    /// Connect to all configured relays.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        foreach (var url in _options.RelayUrls)
        {
            var relay = new NostrRelay();
            try
            {
                await relay.ConnectAsync(url, ct);
                _relays.Add(relay);
            }
            catch (Exception)
            {
                await relay.DisposeAsync();
                // Skip relays that fail to connect
            }
        }

        _connected = _relays.Count > 0;

        if (!_connected)
            throw new InvalidOperationException("Failed to connect to any relay");
    }

    /// <summary>
    /// Discover agent capabilities from connected relays.
    /// </summary>
    public async Task<List<AgentCapability>> DiscoverAsync(
        DiscoverOptions? options = null, CancellationToken ct = default)
    {
        EnsureConnected();
        options ??= new DiscoverOptions();

        var filter = new Dictionary<string, object>
        {
            ["kinds"] = new[] { AgentCapability.Kind },
            ["limit"] = options.Limit
        };

        if (!string.IsNullOrEmpty(options.AuthorPubkey))
            filter["authors"] = new[] { options.AuthorPubkey };

        if (!string.IsNullOrEmpty(options.Category))
            filter["#t"] = new[] { options.Category };

        if (options.Since.HasValue)
            filter["since"] = options.Since.Value;

        var capabilities = new List<AgentCapability>();
        var relay = _relays[0]; // Use first available relay

        var subId = await relay.SubscribeAsync(new object[] { filter }, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await foreach (var evt in relay.ListenAsync(cts.Token))
            {
                var json = NostrEvent.ToJson(evt);
                var doc = JsonDocument.Parse(json);
                var cap = AgentCapability.FromNostrEvent(doc.RootElement);
                capabilities.Add(cap);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when timeout fires
        }

        await relay.CloseSubscriptionAsync(subId, ct);
        return capabilities;
    }

    /// <summary>
    /// Publish an agent capability to connected relays.
    /// </summary>
    public async Task<string> PublishCapabilityAsync(
        AgentCapability capability, CancellationToken ct = default)
    {
        EnsureConnected();

        var tags = capability.ToNostrTags();
        var evt = NostrEvent.Create(
            AgentCapability.Kind,
            capability.Description,
            tags,
            _options.PrivateKey);

        foreach (var relay in _relays)
        {
            await relay.PublishAsync(evt, ct);
        }

        return evt.Id;
    }

    /// <summary>
    /// Send a service request to a capability provider.
    /// </summary>
    public async Task<string> RequestServiceAsync(
        string capabilityId, int budgetSats,
        Dictionary<string, string>? parameters = null,
        CancellationToken ct = default)
    {
        EnsureConnected();

        var tags = new List<string[]>
        {
            new[] { "e", capabilityId },
            new[] { "budget", budgetSats.ToString() },
            new[] { "status", "pending" }
        };

        if (parameters != null)
        {
            foreach (var kvp in parameters)
                tags.Add(new[] { "param", kvp.Key, kvp.Value });
        }

        var evt = NostrEvent.Create(
            AgentServiceRequest.Kind,
            "",
            tags.ToArray(),
            _options.PrivateKey);

        foreach (var relay in _relays)
        {
            await relay.PublishAsync(evt, ct);
        }

        return evt.Id;
    }

    /// <summary>
    /// Settle a service agreement by paying the L402 endpoint.
    /// </summary>
    public async Task<HttpResponseMessage> SettleAsync(
        AgentServiceAgreement agreement, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(agreement.L402Endpoint))
            throw new ArgumentException("Agreement has no L402 endpoint");

        var httpClient = _options.HttpClient ?? new HttpClient();
        var response = await httpClient.GetAsync(agreement.L402Endpoint, ct);
        return response;
    }

    /// <summary>
    /// Create an L402 challenge for a service agreement (producer side).
    /// </summary>
    public async Task<L402ChallengeResponse> CreateChallengeAsync(
        AgentServiceAgreement agreement, int priceSats, string description,
        CancellationToken ct = default)
    {
        if (_producerClient == null)
            throw new InvalidOperationException("Producer client not configured. Set LightningEnableApiKey.");

        return await _producerClient.CreateChallengeAsync(priceSats, description, agreement.Id, ct);
    }

    /// <summary>
    /// Verify a payment was made (producer side).
    /// </summary>
    public async Task<bool> VerifyPaymentAsync(
        string macaroon, string preimage, CancellationToken ct = default)
    {
        if (_producerClient == null)
            throw new InvalidOperationException("Producer client not configured. Set LightningEnableApiKey.");

        return await _producerClient.VerifyPaymentAsync(macaroon, preimage, ct);
    }

    /// <summary>
    /// Publish an attestation (review) for another agent.
    /// </summary>
    public async Task<string> PublishAttestationAsync(
        string subjectPubkey, string agreementId, int rating, string content,
        string? proof = null, CancellationToken ct = default)
    {
        EnsureConnected();

        if (rating < 1 || rating > 5)
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 1 and 5");

        var tags = new List<string[]>
        {
            new[] { "p", subjectPubkey },
            new[] { "e", agreementId },
            new[] { "rating", rating.ToString() },
            new[] { "L", "nostr.agent.attestation" },
            new[] { "l", "completed", "nostr.agent.attestation" },
            new[] { "l", "commerce.service_completion", "nostr.agent.attestation" }
        };

        if (!string.IsNullOrEmpty(proof))
            tags.Add(new[] { "proof", proof });

        var evt = NostrEvent.Create(
            AgentAttestation.Kind,
            content,
            tags.ToArray(),
            _options.PrivateKey);

        foreach (var relay in _relays)
        {
            await relay.PublishAsync(evt, ct);
        }

        return evt.Id;
    }

    /// <summary>
    /// Get the aggregated reputation score for an agent.
    /// </summary>
    public async Task<ReputationScore> GetReputationAsync(
        string pubkey, CancellationToken ct = default)
    {
        EnsureConnected();

        var filter = new Dictionary<string, object>
        {
            ["kinds"] = new[] { AgentAttestation.Kind },
            ["#p"] = new[] { pubkey },
            ["limit"] = 100
        };

        var attestations = new List<AgentAttestation>();
        var relay = _relays[0];

        var subId = await relay.SubscribeAsync(new object[] { filter }, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await foreach (var evt in relay.ListenAsync(cts.Token))
            {
                var json = NostrEvent.ToJson(evt);
                var doc = JsonDocument.Parse(json);
                var att = AgentAttestation.FromNostrEvent(doc.RootElement);
                attestations.Add(att);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await relay.CloseSubscriptionAsync(subId, ct);
        return ReputationScore.FromAttestations(pubkey, attestations);
    }

    private void EnsureConnected()
    {
        if (!_connected || _relays.Count == 0)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var relay in _relays)
        {
            await relay.DisposeAsync();
        }
        _relays.Clear();
        _l402Client.Dispose();
        _producerClient?.Dispose();
    }
}
