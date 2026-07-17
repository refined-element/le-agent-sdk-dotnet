using LightningEnable.AgentSdk.Models;
using LightningEnable.AgentSdk.Nostr;

namespace LightningEnable.AgentSdk.Tests;

/// <summary>
/// Serializes the test classes that redirect the process-global Console.Error so
/// their captures never race under xUnit's default cross-class parallelism. Any
/// class asserting on captured stderr must join this collection.
/// </summary>
[CollectionDefinition("ConsoleCapture", DisableParallelization = true)]
public class ConsoleCaptureCollection { }

/// <summary>
/// Covers NostrRelay.TryParseEventMessage — the wire-message parse that must NEVER
/// throw, so one hostile relay message cannot abort a ListenAsync stream (and with it
/// DiscoverAsync / GetReputationAsync). This is the layer BEFORE signature
/// verification, so an attacker needs no valid signature to reach it (ledger #41).
/// </summary>
[Collection("ConsoleCapture")]
public class NostrRelayTests
{
    private static string EventMessage(string eventBody) => $"[\"EVENT\",\"sub1\",{eventBody}]";

    [Fact]
    public void TryParseEventMessage_ParsesAValidEvent()
    {
        var message = EventMessage(
            "{\"id\":\"abc123\",\"pubkey\":\"def456\",\"created_at\":1700000000," +
            "\"kind\":38400,\"content\":\"hi\",\"tags\":[[\"d\",\"x\"]],\"sig\":\"00\"}");

        var evt = NostrRelay.TryParseEventMessage(message);

        Assert.NotNull(evt);
        Assert.Equal("abc123", evt!.Id);
        Assert.Equal(38400, evt.Kind);
    }

    [Fact]
    public void TryParseEventMessage_SkipsAnEventWhoseCreatedAtIsAString()
    {
        // created_at as a JSON string makes FromJson's GetInt64() throw
        // InvalidOperationException — NOT a JsonException, so the old inline
        // catch (JsonException) let it propagate and abort the whole stream.
        var message = EventMessage(
            "{\"id\":\"abc123\",\"created_at\":\"not-a-number\",\"kind\":38400,\"tags\":[]}");

        var evt = NostrRelay.TryParseEventMessage(message);

        Assert.Null(evt);
    }

    [Fact]
    public void TryParseEventMessage_SkipsAnEventWhoseTagsAreNotAnArray()
    {
        // tags as a JSON string makes FromJson's EnumerateArray() throw
        // InvalidOperationException — again escapes the old JsonException-only catch.
        var message = EventMessage(
            "{\"id\":\"abc123\",\"created_at\":1,\"kind\":38400,\"tags\":\"not-an-array\"}");

        var evt = NostrRelay.TryParseEventMessage(message);

        Assert.Null(evt);
    }

    [Fact]
    public void TryParseEventMessage_SkipsAMessageThatIsNotAJsonArray()
    {
        // Valid JSON, but not a ["<TYPE>", ...] relay message. GetArrayLength() on a
        // JSON object threw InvalidOperationException in the old inline code.
        var evt = NostrRelay.TryParseEventMessage("{\"x\":1}");

        Assert.Null(evt);
    }

    [Fact]
    public void TryParseEventMessage_SkipsNonJsonGarbage()
    {
        var evt = NostrRelay.TryParseEventMessage("this is not json");

        Assert.Null(evt);
    }

    [Fact]
    public void TryParseEventMessage_EoseReturnsNullAndDoesNotWarn()
    {
        // EOSE is a legitimate control message, not an error: it must be skipped
        // QUIETLY (the old code did `continue` silently). Only genuine parse
        // failures warn — this pins that non-EVENT control frames stay silent.
        var originalError = Console.Error;
        var captured = new StringWriter();
        NostrEventData? evt;
        try
        {
            Console.SetError(captured);
            evt = NostrRelay.TryParseEventMessage("[\"EOSE\",\"sub1\"]");
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Null(evt);
        Assert.Equal(string.Empty, captured.ToString());
    }

    [Fact]
    public void TryParseEventMessage_WarnsWhenSkippingAMalformedEvent()
    {
        // A genuinely malformed EVENT must be rejected LOUDLY (fail closed, loudly).
        var originalError = Console.Error;
        var captured = new StringWriter();
        NostrEventData? evt;
        try
        {
            Console.SetError(captured);
            evt = NostrRelay.TryParseEventMessage(EventMessage(
                "{\"id\":\"abc123\",\"created_at\":\"not-a-number\",\"kind\":38400,\"tags\":[]}"));
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Null(evt);
        Assert.Contains("Skipping malformed EVENT", captured.ToString());
    }
}
