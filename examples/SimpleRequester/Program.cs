// Example: Agent that discovers a service and requests it.
//
// This demonstrates how to:
//   1. Discover agent capabilities advertised on Nostr (kind 38400)
//   2. Send a service request to a chosen provider (kind 38401)
//   3. Settle by hitting the L402 endpoint on the resulting agreement
//
// Run with:
//   cd examples/SimpleRequester
//   dotnet run

using LightningEnable.AgentSdk.Agent;

// Replace with your own hex Nostr private key.
// No API key is required for the consumer side.
const string privateKey = "<your_hex_nostr_private_key>";

await using var manager = new AgentManager(new AgentManagerOptions
{
    PrivateKey = privateKey,
    RelayUrls = new List<string> { "wss://agents.lightningenable.com" },
});

await manager.ConnectAsync();
Console.WriteLine($"Connected. Requester pubkey: {manager.Pubkey}");

// Discover translation services on the relay.
Console.WriteLine();
Console.WriteLine("Searching for translation services...");
var capabilities = await manager.DiscoverAsync(new DiscoverOptions
{
    Category = "translation",
    Limit = 10,
});

Console.WriteLine($"Found {capabilities.Count} services");
foreach (var cap in capabilities)
{
    var snippet = cap.Description.Length > 60
        ? cap.Description[..60] + "..."
        : cap.Description;
    Console.WriteLine($"  [{cap.DTag}] {snippet}");
    Console.WriteLine($"    Price:    {cap.PriceSats} sats");
    if (!string.IsNullOrEmpty(cap.Endpoint))
        Console.WriteLine($"    Endpoint: {cap.Endpoint}");
}

if (capabilities.Count == 0)
{
    Console.WriteLine("No services found. Run SimpleProvider first or check the relay.");
    return;
}

// Pick the first capability and send a service request.
var chosen = capabilities[0];
Console.WriteLine();
Console.WriteLine($"Requesting service: {chosen.Name} ({chosen.DTag})");

var requestId = await manager.RequestServiceAsync(
    capabilityId: chosen.Id,
    budgetSats: Math.Max(chosen.PriceSats, 100),
    parameters: new Dictionary<string, string>
    {
        ["source_lang"] = "en",
        ["target_lang"] = "es",
        ["text"] = "Hello, how are you?",
    });

Console.WriteLine($"Sent kind-38401 request: {requestId}");
Console.WriteLine();
Console.WriteLine("In a real flow, the provider responds with a kind-38402 agreement");
Console.WriteLine("containing an L402 endpoint. Call manager.SettleAsync(agreement)");
Console.WriteLine("to hit that endpoint — you'll get a 402, pay the invoice with your");
Console.WriteLine("wallet of choice, and retry with the macaroon+preimage Authorization header.");
