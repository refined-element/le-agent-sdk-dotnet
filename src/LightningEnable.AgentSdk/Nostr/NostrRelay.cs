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
    public async Task<string> SubscribeAsync(object[] filters, CancellationToken ct = default)
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
    public async Task<bool> PublishAsync(NostrEventData evt, CancellationToken ct = default)
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
    public async IAsyncEnumerable<NostrEventData> ListenAsync(
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

            NostrEventData? evt = null;
            try
            {
                var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (root.GetArrayLength() >= 3 && root[0].GetString() == "EVENT")
                {
                    evt = NostrEvent.FromJson(root[2]);
                }
                else if (root.GetArrayLength() >= 2 && root[0].GetString() == "EOSE")
                {
                    // End of stored events, continue listening
                    continue;
                }
            }
            catch (JsonException)
            {
                continue;
            }

            if (evt != null)
                yield return evt;
        }
    }

    /// <summary>
    /// Close a specific subscription.
    /// </summary>
    public async Task CloseSubscriptionAsync(string subId, CancellationToken ct = default)
    {
        if (!_activeSubscriptions.ContainsKey(subId))
            return;

        EnsureConnected();

        var message = JsonSerializer.Serialize(new object[] { "CLOSE", subId });
        await SendAsync(message, ct);
        _activeSubscriptions.Remove(subId);
    }

    public async ValueTask DisposeAsync()
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
