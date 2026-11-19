# LibKolibri

**A drop-in .NET 8 connector for [Kingdom of Loathing](https://www.kingdomofloathing.com/).**

LibKolibri gives a .NET tool the same plumbing that
[KoLmafia](https://github.com/kolmafia/kolmafia) gives its scripts: the
complete KoLmafia data snapshot, authenticated account reads, mall and
price lookups, the daily-routine planners, the mall-arbitrage
executors, a bounded adventure runner and a tamper-evident audit
ledger — all behind a small, stable public API.

```csharp
using LibKolibri;
using KolArbitrage.Core.Integration;

var runtime = await McpRuntimeBootstrap.CreateConfiguredAsync();
runtime.TryGet<IAccountReadClient>(AccountRole.Main, out var account);

var status = await account.FetchStatusAsync();
Console.WriteLine($"{status.PlayerName}: {status.Meat} meat");
```

## Install

```xml
<PackageReference Include="KoLibri.LibKolibri" Version="0.1.0-preview" />
```

The package targets `net8.0`. `KoLibri.Core` comes in as a dependency —
it carries the domain logic (planners, arbitators, runner, audit), while
this package is the façade: runtime, contracts and role scoping.

## What is in the box

| Area | What you get |
|---|---|
| **Data** | The complete KoLmafia data dump as JSON Lines: 12 103 items, 2 366 monsters, 1 657 skills, 3 015 effects, 601 adventures, 148 zones, 13 375 modifiers, 2 238 concoctions, 2 214 monster parts, 164 restores. Loaded into typed records. |
| **Account reads** | Status, inventory, equipment, familiar, skills, effects — read-only, rate-limited, with a stale-cache fallback. |
| **Mall & pricing** | Mall search, own-store reads, pricegun history, a price provider that plugs into the pricing engine. |
| **Daily routines** | Campfire leaves and the Day Shortener, the hermit permit / 11-leaf clover flow, the internet-shop pulls, foreign bribes. The planners are pure functions over a snapshot: they never touch the network. |
| **Mall arbitrage** | Clover restock (buy cheap, keep the main account stocked), the Infinite BACON machine and BACON arbitrage, plus the budget, price-ceiling and reserve rules that keep a purchase safe. |
| **Adventure runner** | A bounded runner that submits only allowlisted actions, stops after a bounded number of choices and never replays a request. |
| **Audit** | An append-only journal with a SHA-256 hash chain over the rows. `VerifyChain()` reports the first tampered row. |
| **Transfer** | Main ↔ Mallbot transfers with a preview/approval/commit flow and a dry-run default. |

## The sample

`sample/KolLibri.Sample` is a runnable consumer that shows the three
things most tools need: build a runtime from the stored sessions, read
the live account, and plan the daily routine from the live numbers.

```bash
dotnet run --project sample/KolLibri.Sample -- --role main
```

## API overview

| Type | Purpose |
|---|---|
| `LibKolibriRuntime` | The root object. Owns the registered services. |
| `RoleScopedRuntime` | Resolves a service per role (`Main`, `Mallbot`) with a fallback. `TryGet<T>(role, out var service)`. |
| `McpRuntimeBootstrap` | Builds a runtime from the encrypted sessions on disk. One read client per role that has valid credentials. |
| `CoreGatewayAdapters` | Adapters that expose the core gateways through the public contracts. |
| `MainMallbotTransferGatewayAdapter` | The transfer gateway behind the public contract. |

The domain types live in `KoLibri.Core` and are re-exported through the
contracts in this package — you never need to reference the core
assembly directly.

## Safety model

This library talks to a game account that holds real value. Three rules
are built in and are not optional:

1. **Reads are free, writes are gated.** Every mutating path requires an
   approval token. A tool that reads without a token simply cannot
   write.
2. **Credentials never appear in code.** Sessions are stored encrypted in
   the user's local app-data folder. The library reads them at runtime and
   keeps them inside the authenticated HTTP clients.
3. **Every write is auditable.** Actions land in a hash-chained journal.
   Each row carries the hash of the row before it, so a tampered log is
   detectable.

The planners and arbitators are deliberately split from the executors:
planning is a pure function over a snapshot, so you can show the operator
exactly what would happen before anything is submitted.

## Related

- **[grassmann-it/KoLibri](https://github.com/grassmann-it/KoLibri)** — the
  full application: CLI, MCP server and Avalonia desktop client built on
  top of this package.
- **KoLmafia** — the original client whose data and behaviour this
  connector mirrors.

## License

MIT — see [LICENSE](LICENSE).
