using System.Text.Json;
using LightningEnable.AgentSdk.Models;

namespace LightningEnable.AgentSdk.Tests;

public class AgentServiceRequestTests
{
    [Fact]
    public void FromNostrEvent_ParsesAllFields()
    {
        var json = """
        {
            "id": "req-123",
            "pubkey": "consumer-pub",
            "created_at": 1700000100,
            "kind": 38402,
            "content": "Please do the thing",
            "tags": [
                ["e", "cap-id-abc"],
                ["p", "provider-pub"],
                ["budget", "500"],
                ["status", "accepted"],
                ["param", "language", "en"],
                ["param", "format", "json"]
            ]
        }
        """;

        var element = JsonDocument.Parse(json).RootElement;
        var req = AgentServiceRequest.FromNostrEvent(element);

        Assert.Equal("req-123", req.Id);
        Assert.Equal("consumer-pub", req.Pubkey);
        Assert.Equal("cap-id-abc", req.CapabilityId);
        Assert.Equal("provider-pub", req.ProviderPubkey);
        Assert.Equal(500, req.BudgetSats);
        Assert.Equal("accepted", req.Status);
        Assert.Equal("en", req.Parameters["language"]);
        Assert.Equal("json", req.Parameters["format"]);
    }

    [Fact]
    public void ToNostrTags_IncludesParameters()
    {
        var req = new AgentServiceRequest
        {
            CapabilityId = "cap-1",
            ProviderPubkey = "prov-1",
            BudgetSats = 300,
            Status = "pending",
            Parameters = new Dictionary<string, string>
            {
                ["key1"] = "value1"
            }
        };

        var tags = req.ToNostrTags();

        Assert.Contains(tags, t => t[0] == "e" && t[1] == "cap-1");
        Assert.Contains(tags, t => t[0] == "budget" && t[1] == "300");
        Assert.Contains(tags, t => t[0] == "param" && t[1] == "key1" && t[2] == "value1");
    }

    [Fact]
    public void Kind_Is38402()
    {
        Assert.Equal(38402, AgentServiceRequest.Kind);
    }
}
