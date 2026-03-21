using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    /// When macaroon is null or empty, uses the MPP Payment authorization scheme
    /// instead of the L402 scheme.
    /// </summary>
    public async Task<L402AccessResult> AccessWithProofAsync(
        string url, string? macaroon, string preimage, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (string.IsNullOrEmpty(macaroon))
        {
            // MPP mode — Payment scheme with preimage only
            ValidatePreimage(preimage);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Payment",
                $"method=\"lightning\", preimage=\"{preimage}\"");
        }
        else
        {
            // L402 mode — macaroon:preimage
            request.Headers.Authorization = new AuthenticationHeaderValue("L402", $"{macaroon}:{preimage}");
        }

        var response = await _httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        return new L402AccessResult
        {
            Success = response.IsSuccessStatusCode,
            StatusCode = (int)response.StatusCode,
            Content = content
        };
    }

    /// <summary>
    /// Validates that a preimage is a safe hex string (no quotes, commas, CR/LF, or other
    /// characters that could break header formatting or enable header injection).
    /// </summary>
    private static void ValidatePreimage(string preimage)
    {
        if (string.IsNullOrEmpty(preimage))
            throw new ArgumentException("Preimage must not be null or empty.", nameof(preimage));

        if (!Regex.IsMatch(preimage, @"^[a-fA-F0-9]+$"))
            throw new ArgumentException(
                "Preimage must be a hex-encoded string (only characters 0-9, a-f, A-F).", nameof(preimage));
    }

    private static L402ChallengeResponse? ParseWwwAuthenticate(HttpHeaderValueCollection<AuthenticationHeaderValue> headers)
    {
        L402ChallengeResponse? mppChallenge = null;

        foreach (var header in headers)
        {
            // Prefer L402 when both L402 and Payment headers are present
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

                // L402 found — return immediately (preferred over MPP)
                return challenge;
            }

            // Check for Payment (MPP) scheme as fallback
            if (header.Scheme.Equals("Payment", StringComparison.OrdinalIgnoreCase) && header.Parameter != null)
            {
                var parameter = header.Parameter;

                var methodMatch = Regex.Match(parameter, @"method=""(?<method>[^""]+)""", RegexOptions.IgnoreCase);
                if (!methodMatch.Success ||
                    !methodMatch.Groups["method"].Value.Equals("lightning", StringComparison.OrdinalIgnoreCase))
                    continue;

                var invoiceMatch = Regex.Match(parameter, @"invoice=""(?<invoice>[^""]+)""", RegexOptions.IgnoreCase);
                if (!invoiceMatch.Success)
                    continue;

                mppChallenge = new L402ChallengeResponse
                {
                    Invoice = invoiceMatch.Groups["invoice"].Value,
                    Macaroon = null,
                    IsMpp = true
                };

                // Parse optional amount — only set PriceSats when the currency is sats (or unspecified, assuming sats by default).
                var amountMatch = Regex.Match(parameter, @"amount=""(?<amount>[^""]+)""", RegexOptions.IgnoreCase);
                if (amountMatch.Success && int.TryParse(amountMatch.Groups["amount"].Value, out var amount))
                {
                    var currencyMatch = Regex.Match(parameter, @"currency=""(?<currency>[^""]+)""", RegexOptions.IgnoreCase);
                    var currency = currencyMatch.Success ? currencyMatch.Groups["currency"].Value : null;

                    if (currency is null ||
                        currency.Equals("sat", StringComparison.OrdinalIgnoreCase) ||
                        currency.Equals("sats", StringComparison.OrdinalIgnoreCase) ||
                        currency.Equals("satoshi", StringComparison.OrdinalIgnoreCase) ||
                        currency.Equals("satoshis", StringComparison.OrdinalIgnoreCase))
                    {
                        mppChallenge.PriceSats = amount;
                    }
                }

                var realmMatch = Regex.Match(parameter, @"realm=""(?<realm>[^""]+)""", RegexOptions.IgnoreCase);
                if (realmMatch.Success)
                    mppChallenge.Description = realmMatch.Groups["realm"].Value;

                // Don't return yet — keep looking for an L402 header which takes priority
            }
        }

        // No L402 found; return MPP challenge if available
        return mppChallenge;
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
