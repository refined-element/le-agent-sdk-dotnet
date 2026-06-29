// Example: Agent that provides a translation service.
//
// This demonstrates how to:
//   1. Publish an agent capability (kind 38400) to Nostr relays
//   2. Create an L402 challenge for incoming requests (producer side)
//   3. Verify a payment once the consumer pays the invoice
//
// Run with:
//   cd examples/SimpleProvider
//   dotnet run

using LightningEnable.AgentSdk.Agent;
using LightningEnable.AgentSdk.Models;

// Replace with your own hex Nostr private key and Lightning Enable API key.
// The API key is required for producer operations (creating/verifying L402
// challenges); it is NOT required for publishing capabilities or discovering
// peers.
const string privateKey = "<your_hex_nostr_private_key>";
const string lightningEnableApiKey = "<your_lightning_enable_api_key>";

await using var manager = new AgentManager(new AgentManagerOptions
{
    PrivateKey = privateKey,
    RelayUrls = new List<string> { "wss://agents.lightningenable.com" },
    LightningEnableApiKey = lightningEnableApiKey,
});

await manager.ConnectAsync();
Console.WriteLine($"Connected. Provider pubkey: {manager.Pubkey}");

// Advertise a capability to the relay.
var capability = new AgentCapability
{
    DTag = "translate-v1",
    Name = "Translation Service",
    Description = "AI translation. 50+ languages. Send text with source/target language params.",
    PriceSats = 10,
    Endpoint = "https://api.example.com/translate",
    Categories = new List<string> { "ai", "translation" },
    Negotiable = false,
};

var eventId = await manager.PublishCapabilityAsync(capability);
Console.WriteLine($"Published capability event {eventId}");
Console.WriteLine($"  Service:    {capability.Name}");
Console.WriteLine($"  Categories: {string.Join(", ", capability.Categories)}");
Console.WriteLine($"  Price:      {capability.PriceSats} sats");

// Producer flow: when a consumer wants the service, mint an L402 challenge
// they can pay. In a real provider, you would call CreateChallengeAsync on
// the agreement you've published in response to a kind-38401 request.
//
// This example demonstrates the L402 producer call in isolation:

var demoAgreement = new AgentServiceAgreement
{
    Id = "agreement-demo-id",
    RequestId = "request-demo-id",
    PriceSats = capability.PriceSats,
    L402Endpoint = "https://api.lightningenable.com/l402/proxy/translate-demo",
};

try
{
    var challenge = await manager.CreateChallengeAsync(
        demoAgreement,
        priceSats: capability.PriceSats,
        description: $"Payment for {capability.Name}");

    Console.WriteLine();
    Console.WriteLine("Issued L402 challenge:");
    var macaroon = challenge.Macaroon ?? "(MPP mode — no macaroon)";
    Console.WriteLine($"  Macaroon: {macaroon[..Math.Min(40, macaroon.Length)]}...");
    Console.WriteLine($"  Invoice:  {challenge.Invoice[..Math.Min(60, challenge.Invoice.Length)]}...");
    Console.WriteLine();
    Console.WriteLine("In a real provider, you would return this challenge to the");
    Console.WriteLine("consumer in a kind-38402 service agreement event. They pay the");
    Console.WriteLine("invoice and call VerifyPaymentAsync(macaroon, preimage) to confirm.");
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Producer client not configured: {ex.Message}");
    Console.Error.WriteLine("Set LightningEnableApiKey in AgentManagerOptions to enable.");
}
