using System.Text;
using System.Text.Json;

namespace LightningEnable.AgentSdk.L402;

/// <summary>
/// Producer-side L402 client. Creates challenges and verifies payments
/// through the Lightning Enable API.
/// </summary>
public class L402ProducerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;
    private readonly string _apiKey;
    private readonly bool _ownsClient;

    public L402ProducerClient(string apiBaseUrl, string apiKey, HttpClient? httpClient = null)
    {
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _apiKey = apiKey;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsClient = true;
        }
    }

    /// <summary>
    /// Create an L402 challenge (invoice + macaroon) for a service.
    /// </summary>
    public async Task<L402ChallengeResponse> CreateChallengeAsync(
        int priceSats, string description, string? agreementId = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["price_sats"] = priceSats,
            ["description"] = description
        };

        if (agreementId != null)
            payload["agreement_id"] = agreementId;

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl}/l402/challenge")
        {
            Content = content
        };
        request.Headers.Add("X-API-Key", _apiKey);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<L402ChallengeResponse>(responseJson, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        return result ?? throw new InvalidOperationException("Failed to parse challenge response");
    }

    /// <summary>
    /// Verify that a payment has been made. When macaroon is null or empty (MPP mode),
    /// sends only the preimage for verification.
    /// </summary>
    /// <param name="macaroon">
    /// The L402 macaroon to verify. When <c>null</c> or empty, the macaroon is omitted and only
    /// the preimage is sent for verification (MPP mode).
    /// </param>
    /// <param name="preimage">The Lightning payment preimage used for verification.</param>
    /// <param name="ct">Optional cancellation token for the HTTP request.</param>
    public async Task<bool> VerifyPaymentAsync(
        string? macaroon, string preimage, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, string>
        {
            ["preimage"] = preimage
        };

        if (!string.IsNullOrEmpty(macaroon))
            payload["macaroon"] = macaroon;

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl}/l402/verify")
        {
            Content = content
        };
        request.Headers.Add("X-API-Key", _apiKey);

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return false;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("valid", out var validProp))
            return validProp.GetBoolean();

        return false;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
