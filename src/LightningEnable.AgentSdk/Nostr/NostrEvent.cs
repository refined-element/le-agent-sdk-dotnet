using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NBitcoin.Secp256k1;

namespace LightningEnable.AgentSdk.Nostr;

/// <summary>
/// Utility class for creating, signing, and verifying Nostr events.
/// </summary>
public static class NostrEvent
{
    /// <summary>
    /// Serialize an event to its canonical NIP-01 form for ID computation:
    /// [0, pubkey, created_at, kind, tags, content]
    /// </summary>
    /// <remarks>
    /// The escaping rules are written out explicitly rather than delegated to
    /// JsonSerializer because the event ID is a cross-implementation consensus
    /// value. NIP-01 escapes only ", \ and the C0 control characters; every other
    /// character — non-ASCII, &lt;, &amp;, +, and astral-plane characters — is
    /// emitted literally. JsonSerializer's default encoder escapes the
    /// HTML-sensitive characters and all non-ASCII, and even
    /// JavaScriptEncoder.UnsafeRelaxedJsonEscaping still escapes characters above
    /// the BMP, so either would compute an ID that no other client agrees with.
    /// </remarks>
    public static string SerializeForId(string pubkey, long createdAt, int kind, string[][] tags, string content)
    {
        var sb = new StringBuilder();

        sb.Append("[0,");
        AppendCanonicalString(sb, pubkey);
        sb.Append(',').Append(createdAt);
        sb.Append(',').Append(kind);
        sb.Append(",[");

        for (var i = 0; i < tags.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[');

            var tag = tags[i];
            for (var j = 0; j < tag.Length; j++)
            {
                if (j > 0) sb.Append(',');
                AppendCanonicalString(sb, tag[j] ?? string.Empty);
            }

            sb.Append(']');
        }

        sb.Append("],");
        AppendCanonicalString(sb, content);
        sb.Append(']');

        return sb.ToString();
    }

    /// <summary>
    /// Append a JSON string literal using NIP-01's escaping rules, matching
    /// JavaScript's JSON.stringify and Python's json.dumps(ensure_ascii=False).
    /// </summary>
    private static void AppendCanonicalString(StringBuilder sb, string value)
    {
        sb.Append('"');

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (ch < 0x20)
                        sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else
                        // Surrogate pairs are appended one UTF-16 unit at a time
                        // and recombine into the original character.
                        sb.Append(ch);
                    break;
            }
        }

        sb.Append('"');
    }

    /// <summary>
    /// Compute the event ID as SHA-256 of the canonical serialization.
    /// [0, pubkey, created_at, kind, tags, content]
    /// </summary>
    public static string ComputeId(string pubkey, long createdAt, int kind, string[][] tags, string content)
    {
        var serialized = SerializeForId(pubkey, createdAt, kind, tags, content);

        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(serialized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Sign an event ID hash with a private key using Schnorr BIP-340.
    /// </summary>
    public static string Sign(string eventIdHex, string privateKeyHex)
    {
        var privKeyBytes = Convert.FromHexString(privateKeyHex);
        using var ecKey = ECPrivKey.Create(privKeyBytes);

        var msgBytes = Convert.FromHexString(eventIdHex);
        if (!ecKey.TrySignBIP340(msgBytes, null, out var sig))
            throw new InvalidOperationException("Failed to create Schnorr signature");

        var sigBytes = new byte[64];
        sig.WriteToSpan(sigBytes);
        return Convert.ToHexString(sigBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Derive the x-only public key (hex) from a private key (hex).
    /// </summary>
    public static string GetPublicKey(string privateKeyHex)
    {
        var privKeyBytes = Convert.FromHexString(privateKeyHex);
        using var ecKey = ECPrivKey.Create(privKeyBytes);
        var pubKey = ecKey.CreateXOnlyPubKey();
        var pubBytes = new byte[32];
        pubKey.WriteToSpan(pubBytes);
        return Convert.ToHexString(pubBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Create a complete Nostr event, optionally signed.
    /// </summary>
    public static NostrEventData Create(int kind, string content, string[][] tags, string? privateKey = null)
    {
        var pubkey = privateKey != null ? GetPublicKey(privateKey) : string.Empty;
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = ComputeId(pubkey, createdAt, kind, tags, content);

        var evt = new NostrEventData
        {
            Id = id,
            Pubkey = pubkey,
            CreatedAt = createdAt,
            Kind = kind,
            Tags = tags,
            Content = content
        };

        if (privateKey != null)
        {
            evt.Sig = Sign(id, privateKey);
        }

        return evt;
    }

    /// <summary>
    /// Verify that an event's ID matches its content and signature is valid.
    /// </summary>
    public static bool Verify(NostrEventData evt)
    {
        // Verify ID
        var computedId = ComputeId(evt.Pubkey, evt.CreatedAt, evt.Kind, evt.Tags, evt.Content);
        if (computedId != evt.Id)
            return false;

        // Verify signature
        if (string.IsNullOrEmpty(evt.Sig))
            return false;

        try
        {
            var pubKeyBytes = Convert.FromHexString(evt.Pubkey);
            if (!ECXOnlyPubKey.TryCreate(pubKeyBytes, out var xOnlyPubKey))
                return false;

            var sigBytes = Convert.FromHexString(evt.Sig);
            if (!SecpSchnorrSignature.TryCreate(sigBytes, out var schnorrSig))
                return false;

            var msgBytes = Convert.FromHexString(evt.Id);
            return xOnlyPubKey.SigVerifyBIP340(schnorrSig, msgBytes);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Serialize a NostrEventData to the JSON format expected by relays.
    /// </summary>
    public static string ToJson(NostrEventData evt)
    {
        var obj = new Dictionary<string, object>
        {
            ["id"] = evt.Id,
            ["pubkey"] = evt.Pubkey,
            ["created_at"] = evt.CreatedAt,
            ["kind"] = evt.Kind,
            ["tags"] = evt.Tags,
            ["content"] = evt.Content,
            ["sig"] = evt.Sig
        };
        return JsonSerializer.Serialize(obj);
    }

    /// <summary>
    /// Parse a NostrEventData from a JSON element.
    /// </summary>
    public static NostrEventData FromJson(JsonElement element)
    {
        var evt = new NostrEventData();

        if (element.TryGetProperty("id", out var idProp))
            evt.Id = idProp.GetString() ?? string.Empty;

        if (element.TryGetProperty("pubkey", out var pubkeyProp))
            evt.Pubkey = pubkeyProp.GetString() ?? string.Empty;

        if (element.TryGetProperty("created_at", out var createdAtProp))
            evt.CreatedAt = createdAtProp.GetInt64();

        if (element.TryGetProperty("kind", out var kindProp))
            evt.Kind = kindProp.GetInt32();

        if (element.TryGetProperty("content", out var contentProp))
            evt.Content = contentProp.GetString() ?? string.Empty;

        if (element.TryGetProperty("sig", out var sigProp))
            evt.Sig = sigProp.GetString() ?? string.Empty;

        if (element.TryGetProperty("tags", out var tagsProp))
        {
            var tagsList = new List<string[]>();
            foreach (var tag in tagsProp.EnumerateArray())
            {
                var tagArray = tag.EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
                tagsList.Add(tagArray);
            }
            evt.Tags = tagsList.ToArray();
        }

        return evt;
    }
}
