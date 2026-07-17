using System.Net;
using LightningEnable.AgentSdk.Agent;
using LightningEnable.AgentSdk.Models;

namespace LightningEnable.AgentSdk.Tests;

/// <summary>
/// SettleAsync is documented as settling an agreement "by paying the L402
/// endpoint", so it must actually pay: a 402 body handed back to the caller is
/// not the service they paid for, and a caller may well treat it as the result.
/// </summary>
public class AgentManagerSettleTests
{
    private const string TestPrivateKey = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
    private const string Preimage = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2";
    private const string Endpoint = "https://provider.example.com/service";

    /// <summary>An L402 endpoint that pays out once the correct proof arrives.</summary>
    private class L402Endpoint : HttpMessageHandler
    {
        private readonly string _invoice;

        public L402Endpoint(string invoice = "lnbc100u1rest") => _invoice = invoice;

        public List<string?> AuthorizationHeaders { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var auth = request.Headers.Authorization?.ToString();
            AuthorizationHeaders.Add(auth);

            if (auth == null)
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.PaymentRequired);
                challenge.Headers.TryAddWithoutValidation(
                    "WWW-Authenticate", $"L402 macaroon=\"testmac\", invoice=\"{_invoice}\"");
                return Task.FromResult(challenge);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("the paid-for service result")
            });
        }
    }

    private static AgentServiceAgreement Agreement() => new() { L402Endpoint = Endpoint };

    [Fact]
    public async Task SettleAsync_PaysTheInvoiceAndReturnsTheServiceResult()
    {
        var handler = new L402Endpoint();
        var paidInvoices = new List<string>();

        var options = new AgentManagerOptions
        {
            PrivateKey = TestPrivateKey,
            HttpClient = new HttpClient(handler),
            PayInvoiceCallback = (invoice, ct) =>
            {
                paidInvoices.Add(invoice);
                return Task.FromResult(Preimage);
            }
        };
        var manager = new AgentManager(options);

        var result = await manager.SettleAsync(Agreement());

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("the paid-for service result", result.Content);
        Assert.Equal(new[] { "lnbc100u1rest" }, paidInvoices);
        // First request unauthenticated, retried with the L402 proof.
        Assert.Equal(2, handler.AuthorizationHeaders.Count);
        Assert.Null(handler.AuthorizationHeaders[0]);
        Assert.Equal($"L402 testmac:{Preimage}", handler.AuthorizationHeaders[1]);
    }

    [Fact]
    public async Task SettleAsync_ThrowsWhenChallengedWithNoPayCallbackConfigured()
    {
        var options = new AgentManagerOptions
        {
            PrivateKey = TestPrivateKey,
            HttpClient = new HttpClient(new L402Endpoint())
        };
        var manager = new AgentManager(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.SettleAsync(Agreement()));
        Assert.Contains("PayInvoiceCallback", ex.Message);
    }

    [Fact]
    public async Task SettleAsync_RefusesAnInvoiceOverTheConfiguredMaximum()
    {
        // lnbc100u = 10,000 sats, over the 5,000 sat ceiling.
        var payCalled = false;
        var options = new AgentManagerOptions
        {
            PrivateKey = TestPrivateKey,
            HttpClient = new HttpClient(new L402Endpoint()),
            MaxAmountSats = 5_000,
            PayInvoiceCallback = (invoice, ct) =>
            {
                payCalled = true;
                return Task.FromResult(Preimage);
            }
        };
        var manager = new AgentManager(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.SettleAsync(Agreement()));
        Assert.Contains("exceeds maximum", ex.Message);
        Assert.False(payCalled);
    }

    [Fact]
    public async Task SettleAsync_RefusesAnInvoiceWithNoAmountWhenAMaximumIsConfigured()
    {
        // An amountless invoice cannot be checked against the budget, and an
        // amount we cannot determine must never be read as "no limit applies".
        var payCalled = false;
        var options = new AgentManagerOptions
        {
            PrivateKey = TestPrivateKey,
            HttpClient = new HttpClient(new L402Endpoint("lnbc1amountless")),
            MaxAmountSats = 5_000,
            PayInvoiceCallback = (invoice, ct) =>
            {
                payCalled = true;
                return Task.FromResult(Preimage);
            }
        };
        var manager = new AgentManager(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.SettleAsync(Agreement()));
        Assert.Contains("no amount", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(payCalled);
    }

    [Fact]
    public async Task SettleAsync_PaysAnInvoiceWithinTheConfiguredMaximum()
    {
        var options = new AgentManagerOptions
        {
            PrivateKey = TestPrivateKey,
            HttpClient = new HttpClient(new L402Endpoint()),
            MaxAmountSats = 10_000,
            PayInvoiceCallback = (invoice, ct) => Task.FromResult(Preimage)
        };
        var manager = new AgentManager(options);

        var result = await manager.SettleAsync(Agreement());

        Assert.True(result.Success);
    }

    [Fact]
    public async Task SettleAsync_ThrowsWhenAgreementHasNoEndpoint()
    {
        var options = new AgentManagerOptions { PrivateKey = TestPrivateKey };
        var manager = new AgentManager(options);

        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.SettleAsync(new AgentServiceAgreement { L402Endpoint = "" }));
    }
}
