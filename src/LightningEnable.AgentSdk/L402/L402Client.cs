using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LightningEnable.AgentSdk.L402;

/// <summary>
/// Consumer-side L402 client. Handles requesting resources protected by L402,
/// detecting 402 challenges, and retrying with payment proof.
/// </summary>
public class L402Client : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public L402Client(HttpClient? httpClient = null)
    {
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
    /// Access an L402-protected endpoint. If a 402 is returned, parses the challenge.
    /// Returns the response (which may be a 402 with challenge info).
    /// </summary>
    public async Task<L402AccessResult> AccessAsync(string url, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            var challenge = ParseWwwAuthenticate(response.Headers.WwwAuthenticate);
            return new L402AccessResult
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                Challenge = challenge
            };
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        return new L402AccessResult
        {
            Success = response.IsSuccessStatusCode,
            StatusCode = (int)response.StatusCode,
            Content = content
        };
    }

    /// <summary>
    /// Access an L402-protected endpoint with a pre-paid macaroon and preimage.
    /// </summary>
    public async Task<L402AccessResult> AccessWithProofAsync(
        string url, string macaroon, string preimage, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("L402", $"{macaroon}:{preimage}");

        var response = await _httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        return new L402AccessResult
        {
            Success = response.IsSuccessStatusCode,
            StatusCode = (int)response.StatusCode,
            Content = content
        };
    }

    private static L402ChallengeResponse? ParseWwwAuthenticate(HttpHeaderValueCollection<AuthenticationHeaderValue> headers)
    {
        foreach (var header in headers)
        {
            if (header.Scheme.Equals("L402", StringComparison.OrdinalIgnoreCase) && header.Parameter != null)
            {
                var challenge = new L402ChallengeResponse();
                var parts = header.Parameter.Split(',', StringSplitOptions.TrimEntries);

                foreach (var part in parts)
                {
                    var kvp = part.Split('=', 2);
                    if (kvp.Length != 2) continue;

                    var key = kvp[0].Trim().Trim('"');
                    var value = kvp[1].Trim().Trim('"');

                    switch (key.ToLowerInvariant())
                    {
                        case "macaroon":
                            challenge.Macaroon = value;
                            break;
                        case "invoice":
                            challenge.Invoice = value;
                            break;
                        case "payment_hash":
                            challenge.PaymentHash = value;
                            break;
                    }
                }

                return challenge;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}

/// <summary>
/// Result of an L402 access attempt.
/// </summary>
public class L402AccessResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string? Content { get; set; }
    public L402ChallengeResponse? Challenge { get; set; }
}
