using System.Text.Json;

namespace LightningEnable.AgentSdk.Models;

/// <summary>
/// Represents an agent attestation/review (NIP kind 38403).
/// Used for reputation scoring between agents after service completion.
/// </summary>
public class AgentAttestation
{
    public string Id { get; set; } = string.Empty;
    public string Pubkey { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public string SubjectPubkey { get; set; } = string.Empty;
    public string AgreementId { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Proof { get; set; }

    public const int Kind = 38403;

    // NIP-32 label constants for attestation events (NIP-A5 spec).
    // The namespace tag declares the label domain; both "l" tags are required:
    //   - "completed" is the status label indicating the service was completed.
    //   - "commerce.service_completion" is the domain label classifying the attestation type.
    public const string Nip32Namespace = "nostr.agent.attestation";
    public const string Nip32StatusLabel = "completed";
    public const string Nip32DomainLabel = "commerce.service_completion";

    /// <summary>
    /// Returns the NIP-32 label tags required by the NIP-A5 spec for attestation events.
    /// </summary>
    public static string[][] GetNip32LabelTags() => new[]
    {
        new[] { "L", Nip32Namespace },
        new[] { "l", Nip32StatusLabel, Nip32Namespace },
        new[] { "l", Nip32DomainLabel, Nip32Namespace }
    };

    public static AgentAttestation FromNostrEvent(JsonElement eventElement)
    {
        var att = new AgentAttestation();

        if (eventElement.TryGetProperty("id", out var idProp))
            att.Id = idProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("pubkey", out var pubkeyProp))
            att.Pubkey = pubkeyProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("created_at", out var createdAtProp))
            att.CreatedAt = createdAtProp.GetInt64();

        if (eventElement.TryGetProperty("content", out var contentProp))
            att.Content = contentProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("tags", out var tagsProp))
        {
            foreach (var tag in tagsProp.EnumerateArray())
            {
                var tagArray = tag.EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
                if (tagArray.Length < 2) continue;

                switch (tagArray[0])
                {
                    case "p":
                        att.SubjectPubkey = tagArray[1];
                        break;
                    case "e":
                        att.AgreementId = tagArray[1];
                        break;
                    case "rating":
                        if (int.TryParse(tagArray[1], out var rating))
                            att.Rating = rating;
                        break;
                    case "proof":
                        att.Proof = tagArray[1];
                        break;
                }
            }
        }

        return att;
    }

    public string[][] ToNostrTags()
    {
        var tags = new List<string[]>
        {
            new[] { "p", SubjectPubkey },
            new[] { "e", AgreementId },
            new[] { "rating", Rating.ToString() }
        };

        tags.AddRange(GetNip32LabelTags());

        if (!string.IsNullOrEmpty(Proof))
            tags.Add(new[] { "proof", Proof });

        return tags.ToArray();
    }
}
