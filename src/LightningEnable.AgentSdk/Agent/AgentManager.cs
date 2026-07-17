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
    /// Test seam: constructs a manager already "connected" to the supplied relays,
    /// so relay behaviour can be exercised without a live WebSocket.
    /// </summary>
    internal AgentManager(AgentManagerOptions options, IEnumerable<NostrRelay> relays)
        : this(options)
    {
        _relays.AddRange(relays);
        _connected = _relays.Count > 0;
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
    /// Publish a signed event to every connected relay.
    /// </summary>
    /// <returns>The event ID, once at least one relay has accepted the event.</returns>
    /// <exception cref="InvalidOperationException">
    /// No relay accepted the event. Returning the ID regardless would make a total
    /// publish failure indistinguishable from a successful publish.
    /// </exception>
    private async Task<string> PublishToRelaysAsync(NostrEventData evt, CancellationToken ct)
    {
        var anyAccepted = false;

        foreach (var relay in _relays)
        {
            try
            {
                if (await relay.PublishAsync(evt, ct))
                    anyAccepted = true;
            }
            catch (Exception)
            {
                // A relay that failed outright simply did not accept the event;
                // keep trying the others.
            }
        }

        if (!anyAccepted)
        {
            throw new InvalidOperationException(
                $"Event {evt.Id} was not accepted by any relay. " +
                $"Tried {_relays.Count} relay(s): {string.Join(", ", _options.RelayUrls)}");
        }

        return evt.Id;
    }

    /// <summary>
    /// Verify that an event carries a valid signature from its claimed author.
    /// Relay lists are caller-configurable and every relay's results are used, so
    /// an unverified event lets a single hostile relay attribute content to any
    /// pubkey. Anything that does not verify is discarded.
    /// </summary>
    private static bool IsAuthentic(NostrEventData evt)
    {
        try
        {
            return NostrEvent.Verify(evt);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Discover agent capabilities from connected relays.
    /// Events that fail signature verification are discarded.
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
                if (!IsAuthentic(evt))
                    continue;

                // Parse each event INDEPENDENTLY. A single malformed capability
                // (e.g. an unparseable price tag) must skip only that one event and
                // never abort discovery — one hostile relay publishing one bad
                // event would otherwise DoS discovery for every agent and discard
                // every capability collected so far. Fail closed, LOUDLY: reject the
                // bad event but make the skip observable. This SDK has no ILogger, so
                // per the repo's existing diagnostic convention (Console.Error, see
                // examples/SimpleProvider) a stderr warning is the minimal way to keep
                // the skip from being silent.
                //
                // This is a hostile-input DoS boundary: the try wraps ONLY the parse
                // of an untrusted event — IsAuthentic/crypto already ran before it and
                // there is no I/O inside — so the only failures possible here are
                // bad-input failures, all of which must be tolerated. Enumerating
                // exception types (FormatException/JsonException) would leave every
                // other throw vector live, which is exactly the mistake logged against
                // the sibling #41 finding ("fixing only the reported line leaves every
                // throw vector live"). The `is not OperationCanceledException` guard
                // catches every parse failure while letting the OCE raised when the
                // timeout above fires still break the loop via the outer catch — the
                // cancellation signal is never swallowed. Matches the Python
                // (`except Exception`) and TS (untyped `catch`) ports.
                AgentCapability cap;
                try
                {
                    var json = NostrEvent.ToJson(evt);
                    var doc = JsonDocument.Parse(json);
                    cap = AgentCapability.FromNostrEvent(doc.RootElement);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine(
                        $"[LightningEnable.AgentSdk] Skipping malformed capability event " +
                        $"{evt.Id}: {ex.Message}");
                    continue;
                }

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

        return await PublishToRelaysAsync(evt, ct);
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

        return await PublishToRelaysAsync(evt, ct);
    }

    /// <summary>
    /// Settle a service agreement by paying the L402 endpoint.
    /// </summary>
    /// <remarks>
    /// Requests the endpoint; if it answers with a payment challenge, pays the
    /// invoice via <see cref="AgentManagerOptions.PayInvoiceCallback"/> and retries
    /// with the proof of payment, so the caller receives the service result rather
    /// than the challenge. When the endpoint needs no payment, its response is
    /// returned unchanged.
    /// </remarks>
    /// <exception cref="ArgumentException">The agreement has no L402 endpoint.</exception>
    /// <exception cref="InvalidOperationException">
    /// Payment is required but no callback is configured, or the invoice exceeds
    /// (or cannot be checked against) <see cref="AgentManagerOptions.MaxAmountSats"/>.
    /// </exception>
    public async Task<L402AccessResult> SettleAsync(
        AgentServiceAgreement agreement, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(agreement.L402Endpoint))
            throw new ArgumentException("Agreement has no L402 endpoint");

        var result = await _l402Client.AccessAsync(agreement.L402Endpoint, ct);

        // Nothing to settle -- the endpoint did not ask for payment.
        if (result.Challenge == null)
            return result;

        var challenge = result.Challenge;

        if (string.IsNullOrEmpty(challenge.Invoice))
        {
            throw new InvalidOperationException(
                $"{agreement.L402Endpoint} demanded payment but supplied no invoice to pay.");
        }

        if (_options.PayInvoiceCallback == null)
        {
            throw new InvalidOperationException(
                $"{agreement.L402Endpoint} requires payment to settle, but no " +
                $"PayInvoiceCallback is configured on AgentManagerOptions. Set one to " +
                $"enable settlement, or use L402Client directly to handle the challenge.");
        }

        EnsureWithinBudget(challenge);

        var preimage = await _options.PayInvoiceCallback(challenge.Invoice, ct);

        return await _l402Client.AccessWithProofAsync(
            agreement.L402Endpoint, challenge.Macaroon, preimage, ct);
    }

    /// <summary>
    /// Hold a challenge to the configured spending ceiling before any payment is made.
    /// </summary>
    private void EnsureWithinBudget(L402ChallengeResponse challenge)
    {
        var max = _options.MaxAmountSats;
        if (max == null)
            return;

        // Prefer an amount the challenge states outright, then fall back to the
        // amount encoded in the invoice itself.
        var amountSats = challenge.PriceSats > 0
            ? challenge.PriceSats
            : L402Client.DecodeInvoiceAmountSats(challenge.Invoice);

        // An amount we cannot determine cannot be checked against the ceiling, and
        // must not be read as "no limit applies" -- an amountless invoice would let
        // the payee claim any amount.
        if (amountSats == null || amountSats <= 0)
        {
            throw new InvalidOperationException(
                $"Invoice has no amount specified. For security, only invoices with " +
                $"explicit amounts are supported when MaxAmountSats ({max} sats) is set. " +
                $"Invoice: {Truncate(challenge.Invoice)}");
        }

        if (amountSats > max)
        {
            throw new InvalidOperationException(
                $"Invoice amount ({amountSats} sats) exceeds maximum allowed ({max} sats). " +
                $"Invoice: {Truncate(challenge.Invoice)}");
        }
    }

    private static string Truncate(string invoice) =>
        invoice.Length <= 40 ? invoice : invoice[..40] + "...";

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
    /// When macaroon is null or empty (MPP mode), verifies using preimage only.
    /// </summary>
    public async Task<bool> VerifyPaymentAsync(
        string? macaroon, string preimage, CancellationToken ct = default)
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
            new[] { "rating", rating.ToString() }
        };

        tags.AddRange(AgentAttestation.GetNip32LabelTags());

        if (!string.IsNullOrEmpty(proof))
            tags.Add(new[] { "proof", proof });

        var evt = NostrEvent.Create(
            AgentAttestation.Kind,
            content,
            tags.ToArray(),
            _options.PrivateKey);

        return await PublishToRelaysAsync(evt, ct);
    }

    /// <summary>
    /// Get the aggregated reputation score for an agent.
    /// Attestations that fail signature verification are discarded.
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
                if (!IsAuthentic(evt))
                    continue;

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
