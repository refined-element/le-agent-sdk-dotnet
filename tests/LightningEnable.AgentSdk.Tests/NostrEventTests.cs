using System.Text.Json;
using LightningEnable.AgentSdk.Nostr;

namespace LightningEnable.AgentSdk.Tests;

public class NostrEventTests
{
    // A known test private key (32 bytes hex) - DO NOT use in production
    private const string TestPrivateKey = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";

    [Fact]
    public void ComputeId_ProducesDeterministicResult()
    {
        var id1 = NostrEvent.ComputeId("pubkey1", 1700000000, 1, Array.Empty<string[]>(), "hello");
        var id2 = NostrEvent.ComputeId("pubkey1", 1700000000, 1, Array.Empty<string[]>(), "hello");

        Assert.Equal(id1, id2);
        Assert.Equal(64, id1.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void ComputeId_DifferentContentProducesDifferentId()
    {
        var id1 = NostrEvent.ComputeId("pubkey1", 1700000000, 1, Array.Empty<string[]>(), "hello");
        var id2 = NostrEvent.ComputeId("pubkey1", 1700000000, 1, Array.Empty<string[]>(), "world");

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void ComputeId_IncludesTags()
    {
        var tags1 = new[] { new[] { "e", "ref1" } };
        var tags2 = new[] { new[] { "e", "ref2" } };

        var id1 = NostrEvent.ComputeId("pub", 1700000000, 1, tags1, "");
        var id2 = NostrEvent.ComputeId("pub", 1700000000, 1, tags2, "");

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void GetPublicKey_ReturnsValid32ByteHex()
    {
        var pubkey = NostrEvent.GetPublicKey(TestPrivateKey);

        Assert.Equal(64, pubkey.Length);
        // Verify it's valid hex
        Assert.True(pubkey.All(c => "0123456789abcdef".Contains(c)));
    }

    [Fact]
    public void GetPublicKey_IsDeterministic()
    {
        var pub1 = NostrEvent.GetPublicKey(TestPrivateKey);
        var pub2 = NostrEvent.GetPublicKey(TestPrivateKey);

        Assert.Equal(pub1, pub2);
    }

    [Fact]
    public void Create_WithPrivateKey_ProducesSignedEvent()
    {
        var evt = NostrEvent.Create(1, "test content", Array.Empty<string[]>(), TestPrivateKey);

        Assert.NotEmpty(evt.Id);
        Assert.NotEmpty(evt.Pubkey);
        Assert.NotEmpty(evt.Sig);
        Assert.Equal(1, evt.Kind);
        Assert.Equal("test content", evt.Content);
        Assert.True(evt.CreatedAt > 0);
    }

    [Fact]
    public void Create_WithoutPrivateKey_ProducesUnsignedEvent()
    {
        var evt = NostrEvent.Create(1, "test", Array.Empty<string[]>());

        Assert.Empty(evt.Pubkey);
        Assert.Empty(evt.Sig);
    }

    [Fact]
    public void Verify_ValidEvent_ReturnsTrue()
    {
        var evt = NostrEvent.Create(1, "verify me", new[] { new[] { "t", "test" } }, TestPrivateKey);

        var result = NostrEvent.Verify(evt);

        Assert.True(result);
    }

    [Fact]
    public void Verify_TamperedContent_ReturnsFalse()
    {
        var evt = NostrEvent.Create(1, "original", Array.Empty<string[]>(), TestPrivateKey);
        evt.Content = "tampered";

        var result = NostrEvent.Verify(evt);

        Assert.False(result);
    }

    [Fact]
    public void Verify_NoSignature_ReturnsFalse()
    {
        var evt = NostrEvent.Create(1, "no sig", Array.Empty<string[]>());

        var result = NostrEvent.Verify(evt);

        Assert.False(result);
    }

    [Fact]
    public void ToJson_ProducesValidJson()
    {
        var evt = NostrEvent.Create(1, "json test", Array.Empty<string[]>(), TestPrivateKey);
        var json = NostrEvent.ToJson(evt);

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(evt.Id, root.GetProperty("id").GetString());
        Assert.Equal(evt.Pubkey, root.GetProperty("pubkey").GetString());
        Assert.Equal(evt.Kind, root.GetProperty("kind").GetInt32());
        Assert.Equal(evt.Content, root.GetProperty("content").GetString());
    }

    [Fact]
    public void FromJson_ParsesCorrectly()
    {
        var original = NostrEvent.Create(1, "parse test", new[] { new[] { "t", "tag1" } }, TestPrivateKey);
        var json = NostrEvent.ToJson(original);
        var element = JsonDocument.Parse(json).RootElement;

        var parsed = NostrEvent.FromJson(element);

        Assert.Equal(original.Id, parsed.Id);
        Assert.Equal(original.Pubkey, parsed.Pubkey);
        Assert.Equal(original.Kind, parsed.Kind);
        Assert.Equal(original.Content, parsed.Content);
        Assert.Equal(original.Sig, parsed.Sig);
        Assert.Single(parsed.Tags);
        Assert.Equal("t", parsed.Tags[0][0]);
        Assert.Equal("tag1", parsed.Tags[0][1]);
    }

    [Fact]
    public void Sign_ProducesValidSignature()
    {
        var pubkey = NostrEvent.GetPublicKey(TestPrivateKey);
        var id = NostrEvent.ComputeId(pubkey, 1700000000, 1, Array.Empty<string[]>(), "sign test");
        var sig = NostrEvent.Sign(id, TestPrivateKey);

        Assert.Equal(128, sig.Length); // 64 bytes = 128 hex chars
    }
}
