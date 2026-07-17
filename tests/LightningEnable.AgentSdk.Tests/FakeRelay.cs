using System.Runtime.CompilerServices;
using LightningEnable.AgentSdk.Nostr;

namespace LightningEnable.AgentSdk.Tests;

/// <summary>
/// A NostrRelay stand-in that answers from memory instead of a live WebSocket,
/// so relay-handling behaviour can be exercised deterministically.
/// </summary>
internal class FakeRelay : NostrRelay
{
    private readonly bool _acceptsPublishes;
    private readonly Exception? _publishFailure;
    private readonly IReadOnlyList<NostrEventData> _eventsToStream;

    public FakeRelay(
        bool acceptsPublishes = true,
        Exception? publishFailure = null,
        IReadOnlyList<NostrEventData>? eventsToStream = null)
    {
        _acceptsPublishes = acceptsPublishes;
        _publishFailure = publishFailure;
        _eventsToStream = eventsToStream ?? Array.Empty<NostrEventData>();
    }

    /// <summary>Events this relay was asked to publish.</summary>
    public List<NostrEventData> PublishedEvents { get; } = new();

    public override Task<bool> PublishAsync(NostrEventData evt, CancellationToken ct = default)
    {
        PublishedEvents.Add(evt);

        if (_publishFailure != null)
            throw _publishFailure;

        // A real relay returns false when it rejects the event or the OK times out.
        return Task.FromResult(_acceptsPublishes);
    }

    public override Task<string> SubscribeAsync(object[] filters, CancellationToken ct = default)
        => Task.FromResult("fake-sub");

    public override async IAsyncEnumerable<NostrEventData> ListenAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in _eventsToStream)
        {
            ct.ThrowIfCancellationRequested();
            yield return evt;
        }

        await Task.CompletedTask;
    }

    public override Task CloseSubscriptionAsync(string subId, CancellationToken ct = default)
        => Task.CompletedTask;

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
