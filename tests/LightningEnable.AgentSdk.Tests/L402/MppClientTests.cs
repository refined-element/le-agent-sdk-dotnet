using System.Net;
using System.Net.Http.Headers;
using LightningEnable.AgentSdk.L402;

namespace LightningEnable.AgentSdk.Tests.L402;

public class MppClientTests
{
    #region ParseWwwAuthenticate — MPP support

    [Fact]
    public async Task Access_MppOnly_ParsesPaymentChallenge()
    {
        // Arrange: server returns only a Payment (MPP) WWW-Authenticate header
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "realm=\"weather-api\", method=\"lightning\", invoice=\"lnbc100n1test\", amount=\"100\", currency=\"sat\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/weather");

        // Assert
        Assert.False(result.Success);
        Assert.Equal(402, result.StatusCode);
        Assert.NotNull(result.Challenge);
        Assert.True(result.Challenge!.IsMpp);
        Assert.Null(result.Challenge.Macaroon);
        Assert.Equal("lnbc100n1test", result.Challenge.Invoice);
        Assert.Equal(100, result.Challenge.PriceSats);
        Assert.Equal("weather-api", result.Challenge.Description);
    }

    [Fact]
    public async Task Access_DualHeaders_PrefersL402OverMpp()
    {
        // Arrange: server returns both L402 and Payment headers
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "realm=\"api\", method=\"lightning\", invoice=\"lnbc_mpp\", amount=\"50\", currency=\"sat\""));
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("L402",
                    "macaroon=\"mac123\", invoice=\"lnbc_l402\", payment_hash=\"hash456\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/data");

        // Assert — should pick L402, not MPP
        Assert.False(result.Success);
        Assert.Equal(402, result.StatusCode);
        Assert.NotNull(result.Challenge);
        Assert.False(result.Challenge!.IsMpp);
        Assert.Equal("mac123", result.Challenge.Macaroon);
        Assert.Equal("lnbc_l402", result.Challenge.Invoice);
        Assert.Equal("hash456", result.Challenge.PaymentHash);
    }

    [Fact]
    public async Task Access_L402OnlyHeader_ReturnsL402Challenge()
    {
        // Arrange: server returns only L402 header (backward compatibility)
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("L402",
                    "macaroon=\"macabc\", invoice=\"lnbc_only\", payment_hash=\"hashdef\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/data");

        // Assert
        Assert.NotNull(result.Challenge);
        Assert.False(result.Challenge!.IsMpp);
        Assert.Equal("macabc", result.Challenge.Macaroon);
        Assert.Equal("lnbc_only", result.Challenge.Invoice);
    }

    [Fact]
    public async Task Access_PaymentHeaderNonLightning_ReturnsNull()
    {
        // Arrange: Payment header with unsupported method
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "realm=\"api\", method=\"stripe\", invoice=\"inv123\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/data");

        // Assert — non-lightning method should not parse as MPP
        Assert.False(result.Success);
        Assert.Equal(402, result.StatusCode);
        Assert.Null(result.Challenge);
    }

    [Fact]
    public async Task Access_MppNonSatCurrency_DoesNotSetPriceSats()
    {
        // Arrange: server returns a Payment header with currency="msat" (not sats)
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "realm=\"api\", method=\"lightning\", invoice=\"lnbc200n1test\", amount=\"200000\", currency=\"msat\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/data");

        // Assert — amount should NOT be set because currency is not sats (remains default 0)
        Assert.NotNull(result.Challenge);
        Assert.True(result.Challenge!.IsMpp);
        Assert.Equal("lnbc200n1test", result.Challenge.Invoice);
        Assert.Equal(0, result.Challenge.PriceSats);
    }

    [Fact]
    public async Task Access_MppNoCurrency_DefaultsToSats()
    {
        // Arrange: server returns a Payment header with amount but no currency (defaults to sats)
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "realm=\"api\", method=\"lightning\", invoice=\"lnbc300n1test\", amount=\"300\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/data");

        // Assert — no currency specified, should default to sats
        Assert.NotNull(result.Challenge);
        Assert.True(result.Challenge!.IsMpp);
        Assert.Equal("lnbc300n1test", result.Challenge.Invoice);
        Assert.Equal(300, result.Challenge.PriceSats);
    }

    [Fact]
    public async Task Access_PaymentHeaderNoInvoice_ReturnsNull()
    {
        // Arrange: Payment header with method=lightning but no invoice
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "realm=\"api\", method=\"lightning\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/data");

        // Assert
        Assert.Null(result.Challenge);
    }

    [Fact]
    public async Task Access_MppWithWhitespaceAroundEquals_ParsesCorrectly()
    {
        // Arrange: server returns Payment header with whitespace around '=' (legal per auth-param spec)
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "realm = \"weather-api\", method = \"lightning\", invoice = \"lnbc500n1ws\", amount = \"500\", currency = \"sats\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/weather");

        // Assert
        Assert.NotNull(result.Challenge);
        Assert.True(result.Challenge!.IsMpp);
        Assert.Equal("lnbc500n1ws", result.Challenge.Invoice);
        Assert.Equal(500, result.Challenge.PriceSats);
        Assert.Equal("weather-api", result.Challenge.Description);
    }

    [Fact]
    public async Task Access_MppWithUnquotedValues_ParsesCorrectly()
    {
        // Arrange: server returns Payment header with unquoted token values
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "method=lightning, invoice=\"lnbc750n1uq\", amount=750"));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/data");

        // Assert
        Assert.NotNull(result.Challenge);
        Assert.True(result.Challenge!.IsMpp);
        Assert.Equal("lnbc750n1uq", result.Challenge.Invoice);
        Assert.Equal(750, result.Challenge.PriceSats);
    }

    #endregion

    #region AccessWithProofAsync — MPP auth header

    [Fact]
    public async Task AccessWithProof_MppMode_SetsPaymentHeader()
    {
        // Arrange: capture the Authorization header sent by the client
        string? capturedAuthHeader = null;
        var handler = new StubHandler(req =>
        {
            capturedAuthHeader = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":\"ok\"}")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act — null macaroon triggers MPP mode (preimage must be 64 hex chars / 32 bytes)
        var result = await client.AccessWithProofAsync(
            "https://example.com/api/data", null, "aabbccdd00112233aabbccdd00112233aabbccdd00112233aabbccdd00112233");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedAuthHeader);
        Assert.Contains("Payment", capturedAuthHeader!);
        Assert.Contains("method=\"lightning\"", capturedAuthHeader);
        Assert.Contains("preimage=\"aabbccdd00112233aabbccdd00112233aabbccdd00112233aabbccdd00112233\"", capturedAuthHeader);
        Assert.DoesNotContain("L402", capturedAuthHeader);
    }

    [Fact]
    public async Task AccessWithProof_EmptyMacaroon_SetsPaymentHeader()
    {
        // Arrange
        string? capturedAuthHeader = null;
        var handler = new StubHandler(req =>
        {
            capturedAuthHeader = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act — empty string macaroon also triggers MPP mode (preimage must be 64 hex chars / 32 bytes)
        var result = await client.AccessWithProofAsync(
            "https://example.com/api/data", "", "ddeeff0011223344ddeeff0011223344ddeeff0011223344ddeeff0011223344");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedAuthHeader);
        Assert.Contains("Payment", capturedAuthHeader!);
        Assert.Contains("preimage=\"ddeeff0011223344ddeeff0011223344ddeeff0011223344ddeeff0011223344\"", capturedAuthHeader);
    }

    [Fact]
    public async Task AccessWithProof_L402Mode_SetsL402Header()
    {
        // Arrange: verify that non-null macaroon still uses L402 scheme
        string? capturedAuthHeader = null;
        var handler = new StubHandler(req =>
        {
            capturedAuthHeader = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessWithProofAsync(
            "https://example.com/api/data", "mac_token_123", "preimage_hex_ghi");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedAuthHeader);
        Assert.StartsWith("L402", capturedAuthHeader!);
        Assert.Contains("mac_token_123:preimage_hex_ghi", capturedAuthHeader);
        Assert.DoesNotContain("Payment", capturedAuthHeader);
    }

    #endregion

    #region L402ProducerClient — MPP verify

    [Fact]
    public async Task VerifyPayment_WithoutMacaroon_SendsPreimageOnly()
    {
        // Arrange: capture the request body
        string? capturedBody = null;
        var handler = new StubHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"valid\":true}")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var producer = new L402ProducerClient("https://api.example.com", "test-key", httpClient);

        // Act — null macaroon (MPP mode)
        var valid = await producer.VerifyPaymentAsync(null, "preimage_hex_123");

        // Assert
        Assert.True(valid);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"preimage\"", capturedBody!);
        Assert.DoesNotContain("\"macaroon\"", capturedBody);
    }

    [Fact]
    public async Task VerifyPayment_WithMacaroon_SendsBothFields()
    {
        // Arrange
        string? capturedBody = null;
        var handler = new StubHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"valid\":true}")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var producer = new L402ProducerClient("https://api.example.com", "test-key", httpClient);

        // Act — with macaroon (L402 mode)
        var valid = await producer.VerifyPaymentAsync("mac_token", "preimage_hex_456");

        // Assert
        Assert.True(valid);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"preimage\"", capturedBody!);
        Assert.Contains("\"macaroon\"", capturedBody);
    }

    [Fact]
    public async Task VerifyPayment_EmptyMacaroon_SendsPreimageOnly()
    {
        // Arrange
        string? capturedBody = null;
        var handler = new StubHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"valid\":true}")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var producer = new L402ProducerClient("https://api.example.com", "test-key", httpClient);

        // Act — empty string macaroon (treated as MPP mode)
        var valid = await producer.VerifyPaymentAsync("", "preimage_hex_789");

        // Assert
        Assert.True(valid);
        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("\"macaroon\"", capturedBody!);
    }

    [Fact]
    public async Task AccessWithProof_MppMode_RejectsShortPreimage()
    {
        // Arrange
        var handler = new StubHandler(req => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act & Assert — 16-char hex is too short (must be 64)
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.AccessWithProofAsync("https://example.com/api/data", null, "aabbccdd00112233"));
        Assert.Contains("exactly 64 hex characters", ex.Message);
    }

    [Fact]
    public async Task AccessWithProof_MppMode_RejectsOddLengthPreimage()
    {
        // Arrange
        var handler = new StubHandler(req => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act & Assert — 63 chars is odd-length
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.AccessWithProofAsync("https://example.com/api/data", null, "aabbccdd00112233aabbccdd00112233aabbccdd00112233aabbccdd0011223"));
        Assert.Contains("even number of characters", ex.Message);
    }

    #endregion

    #region ParseWwwAuthenticate — L402 fallback to MPP

    [Fact]
    public async Task Access_IncompleteL402_FallsBackToMpp()
    {
        // Arrange: server returns an L402 header missing macaroon + a valid Payment header
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            // Incomplete L402 — no macaroon field
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("L402",
                    "invoice=\"lnbc_partial\", payment_hash=\"hash789\""));
            // Valid Payment/MPP header
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "method=\"lightning\", invoice=\"lnbc_mpp_fallback\", amount=\"200\", currency=\"sat\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/data");

        // Assert — incomplete L402 should not block valid MPP challenge
        Assert.NotNull(result.Challenge);
        Assert.True(result.Challenge!.IsMpp);
        Assert.Equal("lnbc_mpp_fallback", result.Challenge.Invoice);
        Assert.Equal(200, result.Challenge.PriceSats);
    }

    [Fact]
    public async Task Access_IncompleteL402NoInvoice_FallsBackToMpp()
    {
        // Arrange: server returns an L402 header missing invoice + a valid Payment header
        var handler = new StubHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
            // Incomplete L402 — has macaroon but no invoice
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("L402",
                    "macaroon=\"mac_only\""));
            // Valid Payment/MPP header
            response.Headers.WwwAuthenticate.Add(
                new AuthenticationHeaderValue("Payment",
                    "method=\"lightning\", invoice=\"lnbc_mpp_fb2\", amount=\"150\", currency=\"sats\""));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        using var client = new L402Client(httpClient);

        // Act
        var result = await client.AccessAsync("https://example.com/api/data");

        // Assert — incomplete L402 should not block valid MPP challenge
        Assert.NotNull(result.Challenge);
        Assert.True(result.Challenge!.IsMpp);
        Assert.Equal("lnbc_mpp_fb2", result.Challenge.Invoice);
    }

    #endregion

    #region L402ChallengeResponse model

    [Fact]
    public void ChallengeResponse_IsMpp_DefaultsFalse()
    {
        var response = new L402ChallengeResponse();
        Assert.False(response.IsMpp);
    }

    [Fact]
    public void ChallengeResponse_MppMode_HasNullMacaroon()
    {
        var response = new L402ChallengeResponse
        {
            IsMpp = true,
            Macaroon = null,
            Invoice = "lnbc100n1test"
        };

        Assert.True(response.IsMpp);
        Assert.Null(response.Macaroon);
        Assert.Equal("lnbc100n1test", response.Invoice);
    }

    #endregion

    /// <summary>
    /// Minimal HttpMessageHandler stub for unit testing HTTP interactions.
    /// Supports both sync and async response factories.
    /// </summary>
    private class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = req => Task.FromResult(handler(req));
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
