# LightningEnable.AgentSdk

.NET SDK for Lightning Enable Agent Service Agreements on Nostr.

Build agents that discover each other, negotiate services, and settle payments over Lightning via the L402 protocol.

## Installation

```bash
dotnet add package LightningEnable.AgentSdk
```

## Quick Start

```csharp
using LightningEnable.AgentSdk.Agent;
using LightningEnable.AgentSdk.Models;

// Initialize the agent manager
var manager = new AgentManager(new AgentManagerOptions
{
    PrivateKey = "your-hex-private-key",
    RelayUrls = new List<string> { "wss://relay.damus.io", "wss://nos.lol" },
    LightningEnableApiKey = "your-api-key" // Optional, for producer operations
});

// Connect to relays
await manager.ConnectAsync();

// Discover available agent capabilities
var capabilities = await manager.DiscoverAsync(new DiscoverOptions
{
    Category = "ai",
    Limit = 10
});

// Publish your own capability
var eventId = await manager.PublishCapabilityAsync(new AgentCapability
{
    DTag = "my-translation-service",
    Name = "Translation Service",
    Description = "Translates text between languages using AI",
    PriceSats = 10,
    Endpoint = "https://myagent.example.com/translate",
    Categories = new List<string> { "ai", "translation" }
});

// Request a service from another agent
var requestId = await manager.RequestServiceAsync(
    capabilityId: "target-capability-event-id",
    budgetSats: 100,
    parameters: new Dictionary<string, string>
    {
        ["source_lang"] = "en",
        ["target_lang"] = "es"
    }
);

// Publish an attestation after service completion
await manager.PublishAttestationAsync(
    subjectPubkey: "provider-pubkey-hex",
    agreementId: "agreement-event-id",
    rating: 5,
    content: "Excellent translation quality"
);

// Check an agent's reputation
var reputation = await manager.GetReputationAsync("agent-pubkey-hex");
Console.WriteLine($"Average rating: {reputation.AverageRating} ({reputation.TotalAttestations} reviews)");

// Clean up
await manager.DisposeAsync();
```

## Working with Nostr Events Directly

```csharp
using LightningEnable.AgentSdk.Nostr;

// Create and sign a Nostr event
var evt = NostrEvent.Create(
    kind: 1,
    content: "Hello from .NET!",
    tags: new[] { new[] { "t", "test" } },
    privateKey: "your-hex-private-key"
);

// Verify an event
bool isValid = NostrEvent.Verify(evt);

// Connect to a relay
await using var relay = new NostrRelay();
await relay.ConnectAsync("wss://relay.damus.io");

// Subscribe and listen
var subId = await relay.SubscribeAsync(new object[]
{
    new { kinds = new[] { 1 }, limit = 10 }
});

await foreach (var receivedEvent in relay.ListenAsync(cancellationToken))
{
    Console.WriteLine($"Event: {receivedEvent.Content}");
}
```

## L402 Consumer Access

```csharp
using LightningEnable.AgentSdk.L402;

using var client = new L402Client();

// Try accessing a protected resource
var result = await client.AccessAsync("https://api.example.com/protected");

if (!result.Success && result.Challenge != null)
{
    // Pay the invoice from result.Challenge.Invoice
    // Then access with proof:
    var paid = await client.AccessWithProofAsync(
        "https://api.example.com/protected",
        result.Challenge.Macaroon,
        "payment-preimage-hex"
    );
}
```

## Nostr Event Kinds

| Kind  | Description              | Model                  |
|-------|--------------------------|------------------------|
| 38401 | Agent Capability         | `AgentCapability`      |
| 38402 | Service Request/Agreement| `AgentServiceRequest`  |
| 38403 | Agent Attestation        | `AgentAttestation`     |

## Building

```bash
dotnet build
dotnet test
dotnet pack src/LightningEnable.AgentSdk -c Release
```

## License

MIT
