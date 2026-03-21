using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace LightningEnable.AgentSdk.L402;

/// <summary>
/// Consumer-side L402 client. Handles requesting resources protected by L402,
/// detecting 402 challenges, and retrying with payment proof.
/// </summary>
public partial class L402Client : IDisposable
{
    private static readonly Regex PreimageRegex = GetPreimageRegex();
    private static readonly Regex AuthParamRegex = GetAuthParamRegex();

    [GeneratedRegex(@"^[a-fA-F0-9]+$")]
    private static partial Regex GetPreimageRegex();

    [GeneratedRegex(@"([!#$%&'*+\-.^_`|~0-9A-Za-z]+)\s*=\s*(?:""([^""]*)""|(\S+?))\s*(?:,|$)")]
    private static partial Regex GetAuthParamRegex();

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
        using var response = await _httpClient.GetAsync(url, ct);

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
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

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

        using var response = await _httpClient.SendAsync(request, ct);
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

        if (!PreimageRegex.IsMatch(preimage))
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

                var mppParams = ParseAuthParams(parameter);

                if (!mppParams.TryGetValue("method", out var method) ||
                    !method.Equals("lightning", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!mppParams.TryGetValue("invoice", out var invoice))
                    continue;

                mppChallenge = new L402ChallengeResponse
                {
                    Invoice = invoice,
                    Macaroon = null,
                    IsMpp = true
                };

                // Parse optional amount — only set PriceSats when the currency is sats (or unspecified, assuming sats by default).
                if (mppParams.TryGetValue("amount", out var amountStr) && int.TryParse(amountStr, out var amount))
                {
                    mppParams.TryGetValue("currency", out var currency);

                    if (currency is null ||
                        currency.Equals("sat", StringComparison.OrdinalIgnoreCase) ||
                        currency.Equals("sats", StringComparison.OrdinalIgnoreCase) ||
                        currency.Equals("satoshi", StringComparison.OrdinalIgnoreCase) ||
                        currency.Equals("satoshis", StringComparison.OrdinalIgnoreCase))
                    {
                        mppChallenge.PriceSats = amount;
                    }
                }

                if (mppParams.TryGetValue("realm", out var realmValue))
                    mppChallenge.Description = realmValue;

                // Don't return yet — keep looking for an L402 header which takes priority
            }
        }

        // No L402 found; return MPP challenge if available
        return mppChallenge;
    }

    /// <summary>
    /// Parses auth-param key/value pairs from an authentication header parameter string.
    /// Tolerant of optional whitespace around '=' and supports both quoted and unquoted values.
    /// Example: <c>method="lightning", invoice="lnbc...", amount=100</c>
    /// </summary>
    private static Dictionary<string, string> ParseAuthParams(string parameterString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Match key = "quoted value" or key = unquoted-token, with optional whitespace around '='.
        // Auth-param names are HTTP tokens, which allow characters like - and . in addition to alphanumerics.
        var matches = AuthParamRegex.Matches(parameterString);

        foreach (Match match in matches)
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            result[key] = value;
        }

        return result;
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
