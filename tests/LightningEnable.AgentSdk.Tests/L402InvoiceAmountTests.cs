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
    // 10u = 10 micro-BTC = 1,000 sats
    [InlineData("lnbc10u1rest", 1_000)]
    // 1m = 1 milli-BTC = 100,000 sats
    [InlineData("lnbc1m1rest", 100_000)]
    // 1000n = 1000 nano-BTC = 100 sats
    [InlineData("lnbc1000n1rest", 100)]
    // 2500n = 2500 nano-BTC = 250 sats
    [InlineData("lnbc2500n1rest", 250)]
    // 10000p = 10,000 pico-BTC = 1 sat
    [InlineData("lnbc10000p1rest", 1)]
    // No multiplier => whole BTC: 2 BTC = 200,000,000 sats.
    [InlineData("lnbc21rest", 200_000_000)]
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

    [Theory]
    // Ledger #74 (decoder-disagreement / fail-open): the bech32 separator is the
    // LAST "1", so the true HRP is "lnbc1p5u" — which encodes NO valid amount.
    // The old lazy `^ln\w+?(\d+)([munp])1` regex scanned forward past the
    // separator into the data part and reported a fabricated 500 sats, which
    // then slipped past the #71 positive-amount guard. It must now be null.
    [InlineData("lnbc1p5u1foo")]
    // Same attack on a testnet prefix; "2u1" lives past the separator.
    [InlineData("lntb1p2u1zzz")]
    public void DecodeInvoiceAmountSats_RefusesBogusAmountInDataPart(string invoice)
    {
        Assert.Null(L402Client.DecodeInvoiceAmountSats(invoice));
    }
}
