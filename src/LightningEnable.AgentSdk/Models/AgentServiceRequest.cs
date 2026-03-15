using System.Text.Json;

namespace LightningEnable.AgentSdk.Models;

/// <summary>
/// Represents a service request from a consumer to a provider (NIP kind 38402).
/// </summary>
public class AgentServiceRequest
{
    public string Id { get; set; } = string.Empty;
    public string Pubkey { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public string CapabilityId { get; set; } = string.Empty;
    public string ProviderPubkey { get; set; } = string.Empty;
    public int BudgetSats { get; set; }
    public string Status { get; set; } = "pending";
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string Content { get; set; } = string.Empty;

    public const int Kind = 38402;

    public static AgentServiceRequest FromNostrEvent(JsonElement eventElement)
    {
        var req = new AgentServiceRequest();

        if (eventElement.TryGetProperty("id", out var idProp))
            req.Id = idProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("pubkey", out var pubkeyProp))
            req.Pubkey = pubkeyProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("created_at", out var createdAtProp))
            req.CreatedAt = createdAtProp.GetInt64();

        if (eventElement.TryGetProperty("content", out var contentProp))
            req.Content = contentProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("tags", out var tagsProp))
        {
            foreach (var tag in tagsProp.EnumerateArray())
            {
                var tagArray = tag.EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
                if (tagArray.Length < 2) continue;

                switch (tagArray[0])
                {
                    case "e":
                        req.CapabilityId = tagArray[1];
                        break;
                    case "p":
                        req.ProviderPubkey = tagArray[1];
                        break;
                    case "budget":
                        if (int.TryParse(tagArray[1], out var budget))
                            req.BudgetSats = budget;
                        break;
                    case "status":
                        req.Status = tagArray[1];
                        break;
                    case "param":
                        if (tagArray.Length >= 3)
                            req.Parameters[tagArray[1]] = tagArray[2];
                        break;
                }
            }
        }

        return req;
    }

    public string[][] ToNostrTags()
    {
        var tags = new List<string[]>
        {
            new[] { "e", CapabilityId },
            new[] { "p", ProviderPubkey },
            new[] { "budget", BudgetSats.ToString() },
            new[] { "status", Status }
        };

        foreach (var kvp in Parameters)
            tags.Add(new[] { "param", kvp.Key, kvp.Value });

        return tags.ToArray();
    }
}
