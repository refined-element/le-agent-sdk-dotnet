using System.Text.Json;
using LightningEnable.AgentSdk.Models;

namespace LightningEnable.AgentSdk.Tests;

public class AgentServiceAgreementTests
{
    [Fact]
    public void FromNostrEvent_ParsesAllFields()
    {
        var json = """
        {
            "id": "agr-123",
            "pubkey": "provider-pub",
            "created_at": 1700000200,
            "kind": 38402,
            "content": "Agreement details",
            "tags": [
                ["e", "req-abc"],
                ["p", "consumer-pub"],
                ["p", "provider-pub"],
                ["price", "250"],
                ["l402", "https://pay.example.com/settle"],
                ["status", "active"],
                ["expiration", "1700086400"]
            ]
        }
        """;

        var element = JsonDocument.Parse(json).RootElement;
        var agr = AgentServiceAgreement.FromNostrEvent(element);

        Assert.Equal("agr-123", agr.Id);
        Assert.Equal("req-abc", agr.RequestId);
        Assert.Equal("consumer-pub", agr.ConsumerPubkey);
        Assert.Equal("provider-pub", agr.ProviderPubkey);
        Assert.Equal(250, agr.PriceSats);
        Assert.Equal("https://pay.example.com/settle", agr.L402Endpoint);
        Assert.Equal("active", agr.Status);
        Assert.Equal(1700086400, agr.ExpiresAt);
    }

    [Fact]
    public void ToNostrTags_IncludesL402Endpoint()
    {
        var agr = new AgentServiceAgreement
        {
            RequestId = "req-1",
            ConsumerPubkey = "cpub",
            ProviderPubkey = "ppub",
            PriceSats = 100,
            L402Endpoint = "https://l402.example.com",
            Status = "active",
            ExpiresAt = 1700090000
        };

        var tags = agr.ToNostrTags();

        Assert.Contains(tags, t => t[0] == "l402" && t[1] == "https://l402.example.com");
        Assert.Contains(tags, t => t[0] == "expiration" && t[1] == "1700090000");
        Assert.Contains(tags, t => t[0] == "price" && t[1] == "100");
    }

    [Fact]
    public void ToNostrTags_IncludesPaymentHashWhenCompleted()
    {
        var agr = new AgentServiceAgreement
        {
            RequestId = "req-1",
            ConsumerPubkey = "cpub",
            ProviderPubkey = "ppub",
            PriceSats = 100,
            Status = "completed",
            PaymentHash = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2"
        };

        var tags = agr.ToNostrTags();

        Assert.Contains(tags, t => t[0] == "payment_hash" && t[1] == "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2");
    }

    [Fact]
    public void ToNostrTags_OmitsPaymentHashWhenNotCompleted()
    {
        var agr = new AgentServiceAgreement
        {
            RequestId = "req-1",
            ConsumerPubkey = "cpub",
            ProviderPubkey = "ppub",
            PriceSats = 100,
            Status = "active",
            PaymentHash = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2"
        };

        var tags = agr.ToNostrTags();

        Assert.DoesNotContain(tags, t => t[0] == "payment_hash");
    }

    [Fact]
    public void ToNostrTags_OmitsPaymentHashWhenNull()
    {
        var agr = new AgentServiceAgreement
        {
            RequestId = "req-1",
            ConsumerPubkey = "cpub",
            ProviderPubkey = "ppub",
            PriceSats = 100,
            Status = "completed"
        };

        var tags = agr.ToNostrTags();

        Assert.DoesNotContain(tags, t => t[0] == "payment_hash");
    }

    [Fact]
    public void FromNostrEvent_ParsesPaymentHash()
    {
        var json = """
        {
            "id": "agr-456",
            "pubkey": "provider-pub",
            "created_at": 1700000200,
            "kind": 38402,
            "content": "",
            "tags": [
                ["e", "req-abc"],
                ["p", "consumer-pub"],
                ["p", "provider-pub"],
                ["price", "250"],
                ["status", "completed"],
                ["payment_hash", "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef"]
            ]
        }
        """;

        var element = JsonDocument.Parse(json).RootElement;
        var agr = AgentServiceAgreement.FromNostrEvent(element);

        Assert.Equal("completed", agr.Status);
        Assert.Equal("deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef", agr.PaymentHash);
    }
}
