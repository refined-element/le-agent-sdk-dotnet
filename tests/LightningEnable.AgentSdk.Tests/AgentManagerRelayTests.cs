using LightningEnable.AgentSdk.Agent;
using LightningEnable.AgentSdk.Models;
using LightningEnable.AgentSdk.Nostr;

namespace LightningEnable.AgentSdk.Tests;

/// <summary>
/// Covers what the manager does with relay outcomes: publishes that no relay
/// accepted, and events a relay could not prove were signed by their author.
/// </summary>
public class AgentManagerRelayTests
{
    private const string TestPrivateKey = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
    private const string OtherPrivateKey = "b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3";

    private static AgentManagerOptions Options() => new() { PrivateKey = TestPrivateKey };

    // --- Publish acceptance ---

    [Fact]
    public async Task PublishCapabilityAsync_ThrowsWhenNoRelayAcceptedTheEvent()
    {
        // Every relay rejected the event (or timed out waiting for OK). Reporting
        // the event id here would be indistinguishable from a successful publish.
        var relay = new FakeRelay(acceptsPublishes: false);
        var manager = new AgentManager(Options(), new[] { relay });

        var cap = new AgentCapability { Name = "Test", DTag = "test" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.PublishCapabilityAsync(cap));
        Assert.Contains("not accepted by any relay", ex.Message);
    }

    [Fact]
    public async Task PublishCapabilityAsync_ReturnsEventIdWhenAtLeastOneRelayAccepted()
    {
        var rejecting = new FakeRelay(acceptsPublishes: false);
        var accepting = new FakeRelay(acceptsPublishes: true);
        var manager = new AgentManager(Options(), new NostrRelay[] { rejecting, accepting });

        var cap = new AgentCapability { Name = "Test", DTag = "test" };
        var id = await manager.PublishCapabilityAsync(cap);

        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(id, accepting.PublishedEvents.Single().Id);
    }

    [Fact]
    public async Task PublishCapabilityAsync_TreatsAThrowingRelayAsNotAccepted()
    {
        var relay = new FakeRelay(publishFailure: new InvalidOperationException("relay exploded"));
        var manager = new AgentManager(Options(), new[] { relay });

        var cap = new AgentCapability { Name = "Test", DTag = "test" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.PublishCapabilityAsync(cap));
    }

    [Fact]
    public async Task PublishCapabilityAsync_StillPublishesWhenOneRelayThrows()
    {
        var throwing = new FakeRelay(publishFailure: new InvalidOperationException("relay exploded"));
        var accepting = new FakeRelay(acceptsPublishes: true);
        var manager = new AgentManager(Options(), new NostrRelay[] { throwing, accepting });

        var cap = new AgentCapability { Name = "Test", DTag = "test" };
        var id = await manager.PublishCapabilityAsync(cap);

        Assert.False(string.IsNullOrEmpty(id));
    }

    [Fact]
    public async Task RequestServiceAsync_ThrowsWhenNoRelayAcceptedTheEvent()
    {
        var relay = new FakeRelay(acceptsPublishes: false);
        var manager = new AgentManager(Options(), new[] { relay });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.RequestServiceAsync("cap-id", 100));
        Assert.Contains("not accepted by any relay", ex.Message);
    }

    [Fact]
    public async Task PublishAttestationAsync_ThrowsWhenNoRelayAcceptedTheEvent()
    {
        var relay = new FakeRelay(acceptsPublishes: false);
        var manager = new AgentManager(Options(), new[] { relay });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.PublishAttestationAsync("subject", "agreement", 5, "great"));
        Assert.Contains("not accepted by any relay", ex.Message);
    }

    // --- Signature verification on inbound events ---

    /// <summary>Signs an event, then reattributes it to another pubkey (a forgery).</summary>
    private static NostrEventData ForgedEvent(int kind, string content, string[][] tags)
    {
        var evt = NostrEvent.Create(kind, content, tags, OtherPrivateKey);
        evt.Pubkey = NostrEvent.GetPublicKey(TestPrivateKey);
        return evt;
    }

    [Fact]
    public async Task DiscoverAsync_DropsEventsWithAnInvalidSignature()
    {
        // A hostile relay injects a capability attributed to an agent that never
        // published it. Relay lists are caller-configurable, so one bad relay in
        // the list must not be able to put words in another agent's mouth.
        var genuine = NostrEvent.Create(
            AgentCapability.Kind, "Genuine", new[] { new[] { "d", "genuine" }, new[] { "name", "Genuine" } }, TestPrivateKey);
        var forged = ForgedEvent(
            AgentCapability.Kind, "Forged", new[] { new[] { "d", "forged" }, new[] { "name", "Forged" } });

        var relay = new FakeRelay(eventsToStream: new[] { genuine, forged });
        var manager = new AgentManager(Options(), new[] { relay });

        var caps = await manager.DiscoverAsync();

        Assert.Single(caps);
        Assert.Equal("genuine", caps[0].DTag);
    }

    [Fact]
    public async Task DiscoverAsync_DropsTamperedEvents()
    {
        var tampered = NostrEvent.Create(
            AgentCapability.Kind, "Original", new[] { new[] { "d", "tampered" } }, TestPrivateKey);
        // Relay rewrites the content but keeps the original id and signature.
        tampered.Content = "Tampered";

        var relay = new FakeRelay(eventsToStream: new[] { tampered });
        var manager = new AgentManager(Options(), new[] { relay });

        var caps = await manager.DiscoverAsync();

        Assert.Empty(caps);
    }

    [Fact]
    public async Task DiscoverAsync_DropsUnsignedEvents()
    {
        var unsigned = NostrEvent.Create(
            AgentCapability.Kind, "Unsigned", new[] { new[] { "d", "unsigned" } });

        var relay = new FakeRelay(eventsToStream: new[] { unsigned });
        var manager = new AgentManager(Options(), new[] { relay });

        var caps = await manager.DiscoverAsync();

        Assert.Empty(caps);
    }

    [Fact]
    public async Task DiscoverAsync_KeepsGenuinelySignedEvents()
    {
        var genuine = NostrEvent.Create(
            AgentCapability.Kind, "Genuine", new[] { new[] { "d", "ok" }, new[] { "price", "100" } }, TestPrivateKey);

        var relay = new FakeRelay(eventsToStream: new[] { genuine });
        var manager = new AgentManager(Options(), new[] { relay });

        var caps = await manager.DiscoverAsync();

        Assert.Single(caps);
        Assert.Equal("ok", caps[0].DTag);
        Assert.Equal(100, caps[0].PriceSats);
        Assert.Equal(NostrEvent.GetPublicKey(TestPrivateKey), caps[0].Pubkey);
    }

    // --- Resilience to malformed capability events (ledger #41) ---

    [Fact]
    public async Task DiscoverAsync_SkipsAMalformedPriceEventAndKeepsTheValidOnes()
    {
        // A single genuinely-signed capability event with an unparseable price must
        // not abort the whole DiscoverAsync call — otherwise one hostile relay
        // publishing one bad event DoS-es discovery for every agent. The malformed
        // event is legitimately signed (so it passes IsAuthentic and reaches the
        // parse step) and sits BETWEEN two valid events, proving the loop neither
        // aborts nor discards capabilities collected before the bad one.
        var first = NostrEvent.Create(
            AgentCapability.Kind, "First",
            new[] { new[] { "d", "first" }, new[] { "price", "100" } }, TestPrivateKey);
        var malformed = NostrEvent.Create(
            AgentCapability.Kind, "Malformed",
            new[] { new[] { "d", "malformed" }, new[] { "price", "abc" } }, TestPrivateKey);
        var second = NostrEvent.Create(
            AgentCapability.Kind, "Second",
            new[] { new[] { "d", "second" }, new[] { "price", "200" } }, TestPrivateKey);

        var relay = new FakeRelay(eventsToStream: new[] { first, malformed, second });
        var manager = new AgentManager(Options(), new[] { relay });

        // Capture stderr so we can assert the skip was LOUD, not silent. The fix
        // has no ILogger and warns via Console.Error, so redirect it around the call.
        var originalError = Console.Error;
        var captured = new StringWriter();
        List<AgentCapability> caps;
        try
        {
            Console.SetError(captured);
            caps = await manager.DiscoverAsync();
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(2, caps.Count);
        Assert.Contains(caps, c => c.DTag == "first");
        Assert.Contains(caps, c => c.DTag == "second");
        Assert.DoesNotContain(caps, c => c.DTag == "malformed");

        // The skip must be observable: the warning names the malformed event's id
        // and says it was skipped.
        var warning = captured.ToString();
        Assert.Contains(malformed.Id, warning);
        Assert.Contains("Skipping malformed capability event", warning);
    }

    [Fact]
    public async Task GetReputationAsync_DropsForgedAttestations()
    {
        // A forged 5-star review attributed to a reputable agent.
        var forged = ForgedEvent(AgentAttestation.Kind, "", new[]
        {
            new[] { "p", "subject" },
            new[] { "e", "agreement" },
            new[] { "rating", "5" }
        });

        var relay = new FakeRelay(eventsToStream: new[] { forged });
        var manager = new AgentManager(Options(), new[] { relay });

        var score = await manager.GetReputationAsync("subject");

        Assert.Equal(0, score.TotalAttestations);
        Assert.Equal(0, score.AverageRating);
    }

    [Fact]
    public async Task GetReputationAsync_KeepsGenuineAttestations()
    {
        var genuine = NostrEvent.Create(AgentAttestation.Kind, "", new[]
        {
            new[] { "p", "subject" },
            new[] { "e", "agreement" },
            new[] { "rating", "4" }
        }, TestPrivateKey);

        var relay = new FakeRelay(eventsToStream: new[] { genuine });
        var manager = new AgentManager(Options(), new[] { relay });

        var score = await manager.GetReputationAsync("subject");

        Assert.Equal(1, score.TotalAttestations);
        Assert.Equal(4, score.AverageRating);
    }
}
