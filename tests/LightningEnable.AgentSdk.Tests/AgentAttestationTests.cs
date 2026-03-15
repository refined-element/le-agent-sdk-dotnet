using System.Text.Json;
using LightningEnable.AgentSdk.Models;

namespace LightningEnable.AgentSdk.Tests;

public class AgentAttestationTests
{
    [Fact]
    public void FromNostrEvent_ParsesAllFields()
    {
        var json = """
        {
            "id": "att-123",
            "pubkey": "reviewer-pub",
            "created_at": 1700000300,
            "kind": 38403,
            "content": "Great service, fast delivery",
            "tags": [
                ["p", "subject-pub"],
                ["e", "agreement-id"],
                ["rating", "5"],
                ["proof", "payment-preimage-hex"]
            ]
        }
        """;

        var element = JsonDocument.Parse(json).RootElement;
        var att = AgentAttestation.FromNostrEvent(element);

        Assert.Equal("att-123", att.Id);
        Assert.Equal("reviewer-pub", att.Pubkey);
        Assert.Equal("subject-pub", att.SubjectPubkey);
        Assert.Equal("agreement-id", att.AgreementId);
        Assert.Equal(5, att.Rating);
        Assert.Equal("Great service, fast delivery", att.Content);
        Assert.Equal("payment-preimage-hex", att.Proof);
    }

    [Fact]
    public void ToNostrTags_IncludesProof()
    {
        var att = new AgentAttestation
        {
            SubjectPubkey = "sub-pub",
            AgreementId = "agr-1",
            Rating = 4,
            Proof = "proof-data"
        };

        var tags = att.ToNostrTags();

        Assert.Contains(tags, t => t[0] == "p" && t[1] == "sub-pub");
        Assert.Contains(tags, t => t[0] == "e" && t[1] == "agr-1");
        Assert.Contains(tags, t => t[0] == "rating" && t[1] == "4");
        Assert.Contains(tags, t => t[0] == "proof" && t[1] == "proof-data");
    }

    [Fact]
    public void ToNostrTags_OmitsProofWhenNull()
    {
        var att = new AgentAttestation
        {
            SubjectPubkey = "sub-pub",
            AgreementId = "agr-1",
            Rating = 3
        };

        var tags = att.ToNostrTags();

        Assert.DoesNotContain(tags, t => t[0] == "proof");
    }

    [Fact]
    public void Kind_Is38403()
    {
        Assert.Equal(38403, AgentAttestation.Kind);
    }
}
