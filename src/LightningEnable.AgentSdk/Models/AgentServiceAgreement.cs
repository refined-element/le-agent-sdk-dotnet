using System.Text.Json;

namespace LightningEnable.AgentSdk.Models;

/// <summary>
/// Represents a service agreement between consumer and provider.
/// Contains settlement details including L402 endpoint information.
/// </summary>
public class AgentServiceAgreement
{
    public string Id { get; set; } = string.Empty;
    public string Pubkey { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string ConsumerPubkey { get; set; } = string.Empty;
    public string ProviderPubkey { get; set; } = string.Empty;
    public int PriceSats { get; set; }
    public string L402Endpoint { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string Content { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
    public string? PaymentHash { get; set; }

    public const int AgreementKind = 38402;

    public static AgentServiceAgreement FromNostrEvent(JsonElement eventElement)
    {
        var agreement = new AgentServiceAgreement();

        if (eventElement.TryGetProperty("id", out var idProp))
            agreement.Id = idProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("pubkey", out var pubkeyProp))
            agreement.Pubkey = pubkeyProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("created_at", out var createdAtProp))
            agreement.CreatedAt = createdAtProp.GetInt64();

        if (eventElement.TryGetProperty("content", out var contentProp))
            agreement.Content = contentProp.GetString() ?? string.Empty;

        if (eventElement.TryGetProperty("tags", out var tagsProp))
        {
            foreach (var tag in tagsProp.EnumerateArray())
            {
                var tagArray = tag.EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
                if (tagArray.Length < 2) continue;

                switch (tagArray[0])
                {
                    case "e":
                        agreement.RequestId = tagArray[1];
                        break;
                    case "p":
                        if (string.IsNullOrEmpty(agreement.ConsumerPubkey))
                            agreement.ConsumerPubkey = tagArray[1];
                        else
                            agreement.ProviderPubkey = tagArray[1];
                        break;
                    case "price":
                        if (int.TryParse(tagArray[1], out var price))
                            agreement.PriceSats = price;
                        break;
                    case "l402":
                        agreement.L402Endpoint = tagArray[1];
                        break;
                    case "status":
                        agreement.Status = tagArray[1];
                        break;
                    case "expiration":
                        if (long.TryParse(tagArray[1], out var exp))
                            agreement.ExpiresAt = exp;
                        break;
                    case "payment_hash":
                        agreement.PaymentHash = tagArray[1];
                        break;
                }
            }
        }

        return agreement;
    }

    public string[][] ToNostrTags()
    {
        var tags = new List<string[]>
        {
            new[] { "e", RequestId },
            new[] { "p", ConsumerPubkey },
            new[] { "p", ProviderPubkey },
            new[] { "price", PriceSats.ToString() },
            new[] { "status", Status }
        };

        if (!string.IsNullOrEmpty(L402Endpoint))
            tags.Add(new[] { "l402", L402Endpoint });

        if (ExpiresAt > 0)
            tags.Add(new[] { "expiration", ExpiresAt.ToString() });

        if (Status == "completed" && !string.IsNullOrEmpty(PaymentHash))
            tags.Add(new[] { "payment_hash", PaymentHash });

        return tags.ToArray();
    }
}
