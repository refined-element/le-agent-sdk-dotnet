using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LightningEnable.AgentSdk.Agent;
using LightningEnable.AgentSdk.Models;
using LightningEnable.AgentSdk.Nostr;

namespace LightningEnable.AgentSdk.Tests;

/// <summary>
/// Port-drift conformance tests (.NET port).
///
/// Runs the shared golden vectors in <c>conformance/vectors/</c> through THIS
/// port's own implementation. The same vectors run in the python and TypeScript
/// ports; any port that diverges from the golden fails its own CI, so drift
/// between the three ports is caught automatically instead of by manual
/// cross-reading. See <c>conformance/README.md</c>.
///
/// In the ConsoleCapture collection because the discover-resilience scenarios
/// redirect the process-global Console.Error to assert the skip is loud.
/// </summary>
[Collection("ConsoleCapture")]
public class ConformanceTests
{
    private const string TestPrivateKey = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
    private const string OtherPrivateKey = "b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3";
    private const string ThirdPrivateKey = "c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";

    private static readonly string ConformanceDir =
        Path.Combine(AppContext.BaseDirectory, "conformance");

    private static JsonElement LoadVectorFile(string name)
    {
        var path = Path.Combine(ConformanceDir, "vectors", name);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static IEnumerable<object[]> VectorNames(string file)
    {
        foreach (var v in LoadVectorFile(file).GetProperty("vectors").EnumerateArray())
            yield return new object[] { v.GetProperty("name").GetString()! };
    }

    private static JsonElement Vector(string file, string name)
    {
        foreach (var v in LoadVectorFile(file).GetProperty("vectors").EnumerateArray())
            if (v.GetProperty("name").GetString() == name)
                return v;
        throw new InvalidOperationException($"vector {name} not found in {file}");
    }

    /// <summary>Parse a capability through the entrypoint the vectors target.</summary>
    private static AgentCapability ParseCapability(JsonElement tags)
    {
        var eventObj = new Dictionary<string, object>
        {
            ["id"] = "conformance",
            ["pubkey"] = "p",
            ["created_at"] = 1L,
            ["kind"] = AgentCapability.Kind,
            ["content"] = "",
            ["tags"] = tags,
        };
        var json = JsonSerializer.Serialize(eventObj);
        return AgentCapability.FromNostrEvent(JsonDocument.Parse(json).RootElement);
    }

    // --- Sync guard ---------------------------------------------------------

    [Fact]
    public void VectorsMatchSharedChecksums()
    {
        // CHECKSUMS is identical across all three repos, so this transitively pins
        // the .NET copy to the python and TypeScript copies. Hashing is over
        // LF-normalized bytes so a CRLF checkout does not spuriously fail.
        var expected = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(Path.Combine(ConformanceDir, "CHECKSUMS")))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            expected[parts[1]] = parts[0];
        }

        Assert.NotEmpty(expected);

        foreach (var path in Directory.GetFiles(Path.Combine(ConformanceDir, "vectors"), "*.json"))
        {
            var raw = File.ReadAllBytes(path);
            var normalized = Encoding.UTF8.GetString(raw).Replace("\r\n", "\n").Replace("\r", "\n");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
                .ToLowerInvariant();
            var name = Path.GetFileName(path);
            Assert.True(expected.TryGetValue(name, out var want) && want == hash,
                $"{name} does not match shared CHECKSUMS (edit the canonical copy + " +
                "regenerate CHECKSUMS in all repos)");
        }
    }

    // --- price-tag parsing --------------------------------------------------

    public static IEnumerable<object[]> PriceVectors() => VectorNames("price-tag.json");

    [Theory]
    [MemberData(nameof(PriceVectors))]
    public void PriceTag(string name)
    {
        var vector = Vector("price-tag.json", name);
        var tags = vector.GetProperty("tags");
        var expect = vector.GetProperty("expect");
        var outcome = expect.GetProperty("outcome").GetString();

        if (outcome == "reject")
        {
            Assert.ThrowsAny<Exception>(() => ParseCapability(tags));
            return;
        }

        var cap = ParseCapability(tags);

        if (outcome == "no-price")
        {
            // This port stores a single integer PriceSats; no price recorded == 0.
            Assert.Equal(0, cap.PriceSats);
            return;
        }

        Assert.Equal("ok", outcome);
        // This port models a single PriceSats and does not carry unit/model, so
        // only the amount is asserted (see price-tag.json outcome notes).
        Assert.Equal(expect.GetProperty("priceSats").GetInt32(), cap.PriceSats);
    }

    // --- negotiable-floor parsing ------------------------------------------

    public static IEnumerable<object[]> FloorVectors() => VectorNames("negotiable-floor.json");

    [Theory]
    [MemberData(nameof(FloorVectors))]
    public void NegotiableFloor(string name)
    {
        var vector = Vector("negotiable-floor.json", name);
        var tags = vector.GetProperty("tags");
        var expect = vector.GetProperty("expect");
        var outcome = expect.GetProperty("outcome").GetString();

        if (outcome == "reject")
        {
            Assert.ThrowsAny<Exception>(() => ParseCapability(tags));
            return;
        }

        Assert.Equal("ok", outcome);
        var cap = ParseCapability(tags);
        Assert.Equal(expect.GetProperty("negotiable").GetBoolean(), cap.Negotiable);

        var minProp = expect.GetProperty("minPriceSats");
        if (minProp.ValueKind == JsonValueKind.Null)
            Assert.Null(cap.MinPriceSats);
        else
            Assert.Equal(minProp.GetInt32(), cap.MinPriceSats);
    }

    // --- discover() batch resilience (ledger #41) --------------------------

    [Fact]
    public void DiscoverResilienceScenariosAreCovered()
    {
        var manifest = LoadVectorFile("discover-resilience.json");
        var names = manifest.GetProperty("scenarios").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()).ToHashSet();
        Assert.Equal(
            new HashSet<string?> { "bad-price", "missing-committed-field", "non-dict-payload" },
            names);
        Assert.Equal(2, manifest.GetProperty("expectedSurvivors").GetInt32());
    }

    [Theory]
    [InlineData("bad-price")]
    [InlineData("missing-committed-field")]
    [InlineData("non-dict-payload")]
    public async Task DiscoverResilience(string scenario)
    {
        var manifest = LoadVectorFile("discover-resilience.json");
        var expectedSurvivors = manifest.GetProperty("expectedSurvivors").GetInt32();

        var originalError = Console.Error;
        var captured = new StringWriter();
        int survivors;
        try
        {
            Console.SetError(captured);
            survivors = scenario == "bad-price"
                ? await SurvivorsViaDiscover()
                : SurvivorsViaFrameParse(scenario);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(expectedSurvivors, survivors);
        // Fail closed, LOUDLY: the malformed payload's skip must be observable.
        Assert.Contains("Skipping", captured.ToString());
    }

    /// <summary>
    /// bad-price is dropped at the per-event capability parse, so it is exercised
    /// end-to-end through DiscoverAsync with genuinely-signed events.
    /// </summary>
    private static async Task<int> SurvivorsViaDiscover()
    {
        var valid1 = NostrEvent.Create(AgentCapability.Kind, "Valid A",
            new[] { new[] { "d", "svc-a" }, new[] { "price", "100" } }, TestPrivateKey);
        var poison = NostrEvent.Create(AgentCapability.Kind, "Poison",
            new[] { new[] { "d", "svc-poison" }, new[] { "price", "abc" } }, ThirdPrivateKey);
        var valid2 = NostrEvent.Create(AgentCapability.Kind, "Valid B",
            new[] { new[] { "d", "svc-b" }, new[] { "price", "200" } }, OtherPrivateKey);

        var relay = new FakeRelay(eventsToStream: new[] { valid1, poison, valid2 });
        var manager = new AgentManager(new AgentManagerOptions { PrivateKey = TestPrivateKey },
            new[] { relay });

        var caps = await manager.DiscoverAsync();
        return caps.Count;
    }

    /// <summary>
    /// missing-committed-field and non-dict-payload are hostile WIRE frames dropped
    /// at NostrRelay.TryParseEventMessage (before an event is ever trusted) — the
    /// layer this port meets them at. A [valid, malformed, valid] frame batch must
    /// yield exactly the 2 valid events.
    /// </summary>
    private static int SurvivorsViaFrameParse(string scenario)
    {
        const string validEvent =
            "{\"id\":\"good\",\"pubkey\":\"p\",\"created_at\":1,\"kind\":38400," +
            "\"content\":\"\",\"tags\":[[\"d\",\"svc\"]],\"sig\":\"00\"}";

        string malformedFrame = scenario switch
        {
            // A committed field (created_at) mistyped as a string -> FromJson's
            // GetInt64() throws -> frame skipped.
            "missing-committed-field" =>
                "[\"EVENT\",\"sub\",{\"id\":\"bad\",\"created_at\":\"not-a-number\"," +
                "\"kind\":38400,\"tags\":[]}]",
            // The EVENT payload is a bare string, not an event object -> skipped.
            "non-dict-payload" => "[\"EVENT\",\"sub\",\"not-an-object\"]",
            _ => throw new InvalidOperationException($"unhandled scenario {scenario}"),
        };

        var frames = new[]
        {
            $"[\"EVENT\",\"sub\",{validEvent}]",
            malformedFrame,
            $"[\"EVENT\",\"sub\",{validEvent}]",
        };

        return frames.Count(f => NostrRelay.TryParseEventMessage(f) != null);
    }
}
