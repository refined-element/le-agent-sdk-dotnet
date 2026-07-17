using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace LightningEnable.AgentSdk.Nostr;

/// <summary>
/// WebSocket client for connecting to Nostr relays.
/// Supports subscribing, publishing, and listening for events.
/// </summary>
public class NostrRelay : IAsyncDisposable
{
    private ClientWebSocket? _ws;
    private readonly Dictionary<string, bool> _activeSubscriptions = new();
    private string _url = string.Empty;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    /// <summary>
    /// Connect to a Nostr relay WebSocket.
    /// </summary>
    public async Task ConnectAsync(string url, CancellationToken ct = default)
    {
        _url = url;
        _ws = new ClientWebSocket();
        var uri = new Uri(url);
        await _ws.ConnectAsync(uri, ct);
    }

    /// <summary>
    /// Subscribe to events matching the given filters.
    /// Returns the subscription ID.
    /// </summary>
    public virtual async Task<string> SubscribeAsync(object[] filters, CancellationToken ct = default)
    {
        EnsureConnected();

        var subId = Guid.NewGuid().ToString("N")[..16];
        var request = new object[] { "REQ", subId }.Concat(filters).ToArray();
        var json = JsonSerializer.Serialize(request);

        await SendAsync(json, ct);
        _activeSubscriptions[subId] = true;

        return subId;
    }

    /// <summary>
    /// Publish a signed event to the relay.
    /// Returns true if the relay accepted the event (received OK).
    /// </summary>
    public virtual async Task<bool> PublishAsync(NostrEventData evt, CancellationToken ct = default)
    {
        EnsureConnected();

        var eventJson = JsonSerializer.Deserialize<JsonElement>(NostrEvent.ToJson(evt));
        var message = JsonSerializer.Serialize(new object[] { "EVENT", eventJson });

        await SendAsync(message, ct);

        // Wait for OK response
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            var response = await ReceiveAsync(cts.Token);
            if (response != null)
            {
                var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                if (root.GetArrayLength() >= 3 && root[0].GetString() == "OK")
                {
                    return root[2].GetBoolean();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout waiting for OK
        }

        return false;
    }

    /// <summary>
    /// Listen for events from active subscriptions.
    /// </summary>
    public virtual async IAsyncEnumerable<NostrEventData> ListenAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        EnsureConnected();

        while (!ct.IsCancellationRequested && IsConnected)
        {
            string? message;
            try
            {
                message = await ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (WebSocketException)
            {
                yield break;
            }

            if (message == null)
                continue;

            // Parse each wire message defensively. A valid-JSON-but-hostile-shaped
            // message must skip only that one message, never abort the stream — this
            // is the same one-bad-event DoS boundary as capability parsing, but one
            // layer earlier (before IsAuthentic), so an attacker needs no valid
            // signature to trigger it. TryParseEventMessage never throws, so nothing
            // here can propagate out of the enumerator and abort DiscoverAsync /
            // GetReputationAsync / any other ListenAsync consumer.
            var evt = TryParseEventMessage(message);
            if (evt != null)
                yield return evt;
        }
    }

    /// <summary>
    /// Parse a single relay wire message into a Nostr event, or return null to skip.
    /// </summary>
    /// <remarks>
    /// NEVER throws. The old inline parse caught only <see cref="JsonException"/>, so a
    /// valid-JSON-but-hostile-shaped message (top-level not an array, or an EVENT whose
    /// <c>created_at</c>/<c>kind</c> is a string, or whose <c>tags</c> is not an array)
    /// threw <see cref="InvalidOperationException"/> out of the async enumerator and
    /// aborted the whole listen loop — one unsigned message DoS-ing discovery for every
    /// agent. Everything that is genuinely malformed is skipped LOUDLY (per the repo's
    /// Console.Error diagnostic convention, since this SDK has no ILogger); legitimate
    /// non-EVENT control messages (EOSE / NOTICE / OK / CLOSED) are skipped QUIETLY
    /// because they are not errors. The document is disposed via <c>using</c> — the old
    /// inline code leaked it.
    /// </remarks>
    internal static NostrEventData? TryParseEventMessage(string message)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(message);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine(
                $"[LightningEnable.AgentSdk] Skipping non-JSON relay message: {ex.Message}");
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;

            // Not a recognizable Nostr relay message (["<TYPE>", ...]) — ignore quietly;
            // this is not an error. GetArrayLength/indexing below are guarded by the
            // ValueKind check short-circuiting first.
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 1
                || root[0].ValueKind != JsonValueKind.String)
                return null;

            // EOSE / NOTICE / OK / CLOSED / anything non-EVENT — nothing to yield, and
            // NOT malformed, so it must not warn (preserves the old silent EOSE continue).
            if (root[0].GetString() != "EVENT" || root.GetArrayLength() < 3)
                return null;

            try
            {
                return NostrEvent.FromJson(root[2]);
            }
            catch (Exception ex)
            {
                // Broad by design: root[2] is fully attacker-controlled and FromJson has
                // many non-JsonException throw vectors (GetInt64/GetInt32 on a string,
                // EnumerateArray on a non-array, TryGetProperty on a non-object). No
                // cancellation token flows into this pure parse, so no OperationCanceled
                // is possible here — catching Exception cannot swallow the timeout signal.
                Console.Error.WriteLine(
                    $"[LightningEnable.AgentSdk] Skipping malformed EVENT from relay: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Close a specific subscription.
    /// </summary>
    public virtual async Task CloseSubscriptionAsync(string subId, CancellationToken ct = default)
    {
        if (!_activeSubscriptions.ContainsKey(subId))
            return;

        EnsureConnected();

        var message = JsonSerializer.Serialize(new object[] { "CLOSE", subId });
        await SendAsync(message, ct);
        _activeSubscriptions.Remove(subId);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_ws != null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                catch
                {
                    // Best effort close
                }
            }
            _ws.Dispose();
            _ws = null;
        }
    }

    private void EnsureConnected()
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected to relay. Call ConnectAsync first.");
    }

    private async Task SendAsync(string message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await _ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
