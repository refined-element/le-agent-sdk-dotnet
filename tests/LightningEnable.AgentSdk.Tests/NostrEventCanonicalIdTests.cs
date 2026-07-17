using LightningEnable.AgentSdk.Nostr;

namespace LightningEnable.AgentSdk.Tests;

/// <summary>
/// NIP-01 event IDs are a cross-implementation consensus value: every client must
/// hash the identical canonical serialization or the IDs (and therefore the
/// signatures over them) will not agree.
///
/// The expected IDs below are fixtures produced by the sibling SDKs, which agree
/// with each other byte for byte:
///   TypeScript: JSON.stringify([0, pubkey, created_at, kind, tags, content])
///   Python:     json.dumps([...], separators=(",", ":"), ensure_ascii=False)
///
/// Only ", \\ and the C0 controls are escaped; everything else — including
/// non-ASCII, &lt;, &amp;, + and astral-plane characters — is serialized literally.
/// </summary>
public class NostrEventCanonicalIdTests
{
    private const string Pubkey = "79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798";
    private const long CreatedAt = 1700000000;
    private const int Kind = 38400;

    [Fact]
    public void ComputeId_MatchesSiblingSdks_ForAsciiContent()
    {
        var id = NostrEvent.ComputeId(
            Pubkey, CreatedAt, Kind,
            new[] { new[] { "d", "svc" } },
            "plain ascii service");

        Assert.Equal("86570bb4433decc23552cee31db4dbffaca2304b326107c9b70b4f4443bfe98c", id);
    }

    [Fact]
    public void ComputeId_MatchesSiblingSdks_ForNonAsciiContent()
    {
        // Non-ASCII must be serialized literally, not escaped to \uXXXX.
        var id = NostrEvent.ComputeId(
            Pubkey, CreatedAt, Kind,
            new[] { new[] { "d", "svc" } },
            "Übersetzung 日本語");

        Assert.Equal("26974af501ab790c2b3d1a0d450d5eedabbb746a1a066e9bab6746757548dab4", id);
    }

    [Fact]
    public void ComputeId_MatchesSiblingSdks_ForHtmlSensitiveContent()
    {
        // <, > and & are HTML-sensitive but carry no special meaning in JSON.
        var id = NostrEvent.ComputeId(
            Pubkey, CreatedAt, Kind,
            new[] { new[] { "d", "svc" } },
            "a < b & c + d");

        Assert.Equal("f715379789b94b772855d334b14bc485596ee4bb1c96253528d947e8ac4c4e1a", id);
    }

    [Fact]
    public void ComputeId_MatchesSiblingSdks_ForAstralPlaneContent()
    {
        // Characters above the BMP are a surrogate pair in UTF-16 and must still
        // be emitted literally.
        var id = NostrEvent.ComputeId(
            Pubkey, CreatedAt, Kind,
            new[] { new[] { "d", "svc" } },
            "ship it 🚀");

        Assert.Equal("aba37d989ae1d403b0e4e83f16fb654f2c0d7e0a4fd2368d3e5cb67c793b26c1", id);
    }

    [Fact]
    public void ComputeId_MatchesSiblingSdks_ForNonAsciiTags()
    {
        // Tag values go through the same escaping rules as content.
        var id = NostrEvent.ComputeId(
            Pubkey, CreatedAt, Kind,
            new[] { new[] { "d", "übersetzung" }, new[] { "name", "日本語 sübersetzer" } },
            "");

        Assert.Equal("cd6fb19a9ab97a19d3152a6afe0b3db08710a5b40a2bfd6d771de9e9c3859e03", id);
    }

    [Fact]
    public void ComputeId_EscapesQuotesBackslashesAndControlCharacters()
    {
        // Round-trips through the same rules JSON.stringify applies.
        var serialized = NostrEvent.SerializeForId(
            Pubkey, CreatedAt, Kind,
            Array.Empty<string[]>(),
            "quote\" slash\\ nl\n tab\t");

        Assert.Contains("\\\"", serialized);
        Assert.Contains("\\\\", serialized);
        Assert.Contains("\\n", serialized);
        Assert.Contains("\\t", serialized);
    }

    [Fact]
    public void Verify_AcceptsAnEventSignedByThisSdk()
    {
        // Whatever the escaping rules, Create and Verify must remain consistent.
        const string privateKey = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";

        var evt = NostrEvent.Create(
            Kind, "Übersetzung 日本語 🚀 <ai> & more",
            new[] { new[] { "d", "übersetzung" } },
            privateKey);

        Assert.True(NostrEvent.Verify(evt));
    }
}
