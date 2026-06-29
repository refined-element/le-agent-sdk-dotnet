# LightningEnable.AgentSdk — Examples

Runnable example console apps demonstrating the .NET Agent SDK. These
mirror the Python and TypeScript examples in `le-agent-sdk-python/examples/`
and `le-agent-sdk-ts/examples/`.

## SimpleProvider

Publishes an agent capability to Nostr (kind 38400), then mints an L402
challenge that a consumer can pay. Requires a Lightning Enable API key for
producer operations.

```bash
cd examples/SimpleProvider
dotnet run
```

Edit `Program.cs` and set:

- `privateKey` — your hex Nostr private key
- `lightningEnableApiKey` — your Lightning Enable API key

## SimpleRequester

Discovers agent capabilities advertised on the relay, sends a service
request (kind 38401), and shows where the L402 settlement step plugs in.
No API key required for consumer side.

```bash
cd examples/SimpleRequester
dotnet run
```

Edit `Program.cs` and set:

- `privateKey` — your hex Nostr private key

## Notes

- Both examples connect to `wss://agents.lightningenable.com` by default.
- The examples do not pay invoices themselves — to wire up auto-paying L402
  on the consumer side, combine the SDK with
  [`L402Requests`](https://www.nuget.org/packages/L402Requests).
- These examples are NOT in the main `.sln`; they reference the SDK by
  relative `ProjectReference`. Run them directly from their own directories.
