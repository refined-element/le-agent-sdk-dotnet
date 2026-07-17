using LightningEnable.AgentSdk.L402;

namespace LightningEnable.AgentSdk.Tests;

/// <summary>
/// BOLT-11 amount decoding, used to hold a settlement to its configured budget.
/// </summary>
public class L402InvoiceAmountTests
{
    [Theory]
    // 100u = 100 micro-BTC = 10,000 sats
    [InlineData("lnbc100u1rest", 10_000)]
    // 1m = 1 milli-BTC = 100,000 sats
    [InlineData("lnbc1m1rest", 100_000)]
    // 1000n = 1000 nano-BTC = 100 sats
    [InlineData("lnbc1000n1rest", 100)]
    // 10000p = 10,000 pico-BTC = 1 sat
    [InlineData("lnbc10000p1rest", 1)]
    // Case-insensitive, and the lightning: URI prefix is tolerated.
    [InlineData("LNBC100U1REST", 10_000)]
    [InlineData("lightning:lnbc100u1rest", 10_000)]
    // Testnet / signet / regtest prefixes.
    [InlineData("lntb100u1rest", 10_000)]
    public void DecodeInvoiceAmountSats_DecodesEncodedAmounts(string invoice, int expected)
    {
        Assert.Equal(expected, L402Client.DecodeInvoiceAmountSats(invoice));
    }

    [Theory]
    // An amountless invoice: the payee may claim any amount.
    [InlineData("lnbc1rest")]
    [InlineData("lnbc1")]
    // Not a BOLT-11 invoice at all.
    [InlineData("not-an-invoice")]
    [InlineData("")]
    public void DecodeInvoiceAmountSats_ReturnsNullWhenNoAmountCanBeDetermined(string invoice)
    {
        Assert.Null(L402Client.DecodeInvoiceAmountSats(invoice));
    }
}
