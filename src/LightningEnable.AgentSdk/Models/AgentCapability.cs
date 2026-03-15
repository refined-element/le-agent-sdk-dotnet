using System.Text.Json;

namespace LightningEnable.AgentSdk.Models;

/// <summary>
/// Represents an agent capability advertisement (NIP kind 38401).
/// Describes what service an agent offers and at what price.
/// </summary>
public class AgentCapability
{
    public string Id { get; set; } = string.Empty;
    public string Pubkey { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InputSchema { get; set; } = string.Empty;
    public string OutputSchema { get; set; } = string.Empty;
    public int PriceSats { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string DTag { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = new();

    public const int Kind = 38401;

    /// <summary>
    /// Parse an AgentCapability from a Nostr event JSON element.
    /// </summary>
    public static AgentCapability FromNostrEvent(JsonElement eventElement)
    {
        var cap = new AgentCapability();

        if (eventElement.TryGetProperty("id", out var idProp))
            cap.Id = idProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("pubkey", out var pubkeyProp))
            cap.Pubkey = pubkeyProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("created_at", out var createdAtProp))
            cap.CreatedAt = createdAtProp.GetInt64();

        if (eventElement.TryGetProperty("content", out var contentProp))
            cap.Description = contentProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("tags", out var tagsProp))
        {
            foreach (var tag in tagsProp.EnumerateArray())
            {
                var tagArray = tag.EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
                if (tagArray.Length < 2) continue;

                switch (tagArray[0])
                {
                    case "d":
                        cap.DTag = tagArray[1];
                        break;
                    case "name":
                        cap.Name = tagArray[1];
                        break;
                    case "description":
                        cap.Description = tagArray[1];
                        break;
                    case "input_schema":
                        cap.InputSchema = tagArray[1];
                        break;
                    case "output_schema":
                        cap.OutputSchema = tagArray[1];
                        break;
                    case "price":
                        if (int.TryParse(tagArray[1], out var price))
                            cap.PriceSats = price;
                        break;
                    case "endpoint":
                        cap.Endpoint = tagArray[1];
                        break;
                    case "t":
                        cap.Categories.Add(tagArray[1]);
                        break;
                }
            }
        }

        return cap;
    }

    /// <summary>
    /// Convert this capability to Nostr event tag arrays.
    /// </summary>
    public string[][] ToNostrTags()
    {
        var tags = new List<string[]>
        {
            new[] { "d", DTag },
            new[] { "name", Name },
            new[] { "description", Description },
            new[] { "price", PriceSats.ToString() }
        };

        if (!string.IsNullOrEmpty(InputSchema))
            tags.Add(new[] { "input_schema", InputSchema });

        if (!string.IsNullOrEmpty(OutputSchema))
            tags.Add(new[] { "output_schema", OutputSchema });

        if (!string.IsNullOrEmpty(Endpoint))
            tags.Add(new[] { "endpoint", Endpoint });

        foreach (var cat in Categories)
            tags.Add(new[] { "t", cat });

        return tags.ToArray();
    }
}
