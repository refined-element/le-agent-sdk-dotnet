using System.Text.Json;
using LightningEnable.AgentSdk.Models;

namespace LightningEnable.AgentSdk.Tests;

public class AgentCapabilityTests
{
    [Fact]
    public void FromNostrEvent_ParsesAllFields()
    {
        var json = """
        {
            "id": "abc123",
            "pubkey": "def456",
            "created_at": 1700000000,
            "kind": 38401,
            "content": "A test capability",
            "tags": [
                ["d", "test-cap-1"],
                ["name", "Test Capability"],
                ["description", "Performs testing"],
                ["price", "100"],
                ["input_schema", "{\"type\":\"object\"}"],
                ["output_schema", "{\"type\":\"string\"}"],
                ["endpoint", "https://example.com/api"],
                ["t", "testing"],
                ["t", "automation"]
            ]
        }
        """;

        var element = JsonDocument.Parse(json).RootElement;
        var cap = AgentCapability.FromNostrEvent(element);

        Assert.Equal("abc123", cap.Id);
        Assert.Equal("def456", cap.Pubkey);
        Assert.Equal(1700000000, cap.CreatedAt);
        Assert.Equal("test-cap-1", cap.DTag);
        Assert.Equal("Test Capability", cap.Name);
        Assert.Equal("Performs testing", cap.Description);
        Assert.Equal(100, cap.PriceSats);
        Assert.Equal("{\"type\":\"object\"}", cap.InputSchema);
        Assert.Equal("{\"type\":\"string\"}", cap.OutputSchema);
        Assert.Equal("https://example.com/api", cap.Endpoint);
        Assert.Equal(2, cap.Categories.Count);
        Assert.Contains("testing", cap.Categories);
        Assert.Contains("automation", cap.Categories);
    }

    [Fact]
    public void ToNostrTags_ProducesCorrectTags()
    {
        var cap = new AgentCapability
        {
            DTag = "my-cap",
            Name = "My Capability",
            Description = "Does stuff",
            PriceSats = 50,
            InputSchema = "{\"type\":\"object\"}",
            Endpoint = "https://example.com",
            Categories = new List<string> { "ai", "coding" }
        };

        var tags = cap.ToNostrTags();

        Assert.Contains(tags, t => t[0] == "d" && t[1] == "my-cap");
        Assert.Contains(tags, t => t[0] == "name" && t[1] == "My Capability");
        Assert.Contains(tags, t => t[0] == "price" && t[1] == "50");
        Assert.Contains(tags, t => t[0] == "endpoint" && t[1] == "https://example.com");
        Assert.Contains(tags, t => t[0] == "t" && t[1] == "ai");
        Assert.Contains(tags, t => t[0] == "t" && t[1] == "coding");
    }

    [Fact]
    public void RoundTrip_CapabilityToTagsAndBack()
    {
        var original = new AgentCapability
        {
            DTag = "roundtrip",
            Name = "Round Trip",
            Description = "Test round trip",
            PriceSats = 200,
            Endpoint = "https://rt.example.com"
        };

        var tags = original.ToNostrTags();

        // Build a fake event JSON from the tags
        var eventObj = new
        {
            id = "fake-id",
            pubkey = "fake-pubkey",
            created_at = 1700000000L,
            kind = 38401,
            content = original.Description,
            tags
        };

        var json = JsonSerializer.Serialize(eventObj);
        var element = JsonDocument.Parse(json).RootElement;
        var parsed = AgentCapability.FromNostrEvent(element);

        Assert.Equal(original.DTag, parsed.DTag);
        Assert.Equal(original.Name, parsed.Name);
        Assert.Equal(original.PriceSats, parsed.PriceSats);
        Assert.Equal(original.Endpoint, parsed.Endpoint);
    }

    [Fact]
    public void Kind_Is38401()
    {
        Assert.Equal(38401, AgentCapability.Kind);
    }

    [Fact]
    public void Negotiable_DefaultsToTrue()
    {
        var cap = new AgentCapability();
        Assert.True(cap.Negotiable);
        Assert.Null(cap.MinPriceSats);
        var tags = cap.ToNostrTags();
        Assert.Contains(tags, t => t[0] == "negotiable" && t[1] == "true");
    }

    [Fact]
    public void Negotiable_False_EmitsCorrectTag()
    {
        var cap = new AgentCapability { Negotiable = false };
        var tags = cap.ToNostrTags();
        Assert.Contains(tags, t => t[0] == "negotiable" && t[1] == "false");
    }

    [Fact]
    public void Negotiable_Floor_EmitsCorrectTag()
    {
        var cap = new AgentCapability { Negotiable = true, MinPriceSats = 30000 };
        var tags = cap.ToNostrTags();
        Assert.Contains(tags, t => t[0] == "negotiable" && t[1] == "floor" && t[2] == "30000");
    }

    [Fact]
    public void Negotiable_False_ParsedFromEvent()
    {
        var json = """
        {
            "id": "n1",
            "pubkey": "pk",
            "created_at": 1,
            "kind": 38401,
            "content": "",
            "tags": [
                ["d", "svc"],
                ["negotiable", "false"]
            ]
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;
        var cap = AgentCapability.FromNostrEvent(element);
        Assert.False(cap.Negotiable);
        Assert.Null(cap.MinPriceSats);
    }

    [Fact]
    public void Negotiable_Floor_ParsedFromEvent()
    {
        var json = """
        {
            "id": "n2",
            "pubkey": "pk",
            "created_at": 1,
            "kind": 38401,
            "content": "",
            "tags": [
                ["d", "svc"],
                ["negotiable", "floor", "10000"]
            ]
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;
        var cap = AgentCapability.FromNostrEvent(element);
        Assert.True(cap.Negotiable);
        Assert.Equal(10000, cap.MinPriceSats);
    }
}
