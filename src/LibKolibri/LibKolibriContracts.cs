using KolArbitrage.Core.Adventure;
using KolArbitrage.Core.Integration;

namespace LibKolibri;

/// <summary>Stable capability names exposed by the reusable KoL adapter.</summary>
public enum KolibriCapability
{
    /// <summary>Session and account state.</summary>
    Account,
    /// <summary>Inventory snapshots.</summary>
    Inventory,
    /// <summary>Mall and own-shop observations.</summary>
    Mall,
    /// <summary>Price observations and repricing policies.</summary>
    Pricing,
    /// <summary>Adventure planning and execution adapters.</summary>
    Adventure,
    /// <summary>Approval-bound manual combat adapters.</summary>
    Combat,
    /// <summary>Airport and other currency shops.</summary>
    Coinmasters,
    /// <summary>Cross-account transfer planning and commits.</summary>
    Transfers,
    /// <summary>Own-shop buyer and sales analysis.</summary>
    BuyerAnalytics
}

/// <summary>A read-only account/inventory gateway implemented by a KoL adapter.</summary>
public interface IAccountGateway
{
    /// <summary>Returns a cached or freshly fetched account status snapshot.</summary>
    Task<KoLStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns a cached or freshly fetched inventory snapshot.</summary>
    Task<KoLInventorySnapshot> GetInventoryAsync(CancellationToken cancellationToken = default);
}

/// <summary>Mall and own-shop reads. Mutations belong behind explicit approval gateways.</summary>
public interface IMallGateway
{
    /// <summary>Searches the Mall using the adapter's cache policy.</summary>
    Task<MallSnapshot> SearchAsync(string item, CancellationToken cancellationToken = default);
    /// <summary>Reads current own-shop listings using the adapter's cache policy.</summary>
    Task<IReadOnlyList<OwnShopListing>> GetOwnShopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Adventure planning/execution boundary. Planning is pure; execution is approval-gated.</summary>
public interface IAdventureGateway
{
    /// <summary>Builds a pure daily plan from a read-only adventure snapshot.</summary>
    Task<DailyAdventurePlan> PlanAsync(AdventureState state, CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit manual-combat boundary. Registering this contract is opt-in and
/// does not create a transport; callers still provide the approval-bound Core
/// gateway and may use the bounded runner above it.
/// </summary>
public interface ICombatGateway
{
    Task<ManualCombatInspectionResult> InspectAsync(
        string combatId,
        CancellationToken cancellationToken = default);

    Task<ManualCombatPreview> PreviewAsync(
        ManualCombatRequest request,
        CancellationToken cancellationToken = default);

    Task<ManualCombatExecutionResult> ExecuteAsync(
        ManualCombatPreview preview,
        string? approvalToken = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Currency-shop catalog and resale ranking boundary.</summary>
public interface ICoinmasterGateway
{
    /// <summary>Ranks tradeable Foreign Bribe outputs by expected resale value.</summary>
    Task<IReadOnlyList<ForeignBribeRanking>> RankForeignBribesAsync(
        IReadOnlyDictionary<string, decimal>? currencyValues = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Explicit approval boundary for future cross-account transfers.</summary>
public interface ITransferGateway
{
    /// <summary>Validates a proposed transfer without moving any item.</summary>
    Task<TransferPreview> PreviewAsync(TransferRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Requested item movement between two configured accounts.</summary>
public sealed record TransferRequest(
    string SourceAccount,
    string DestinationAccount,
    IReadOnlyDictionary<string, int> Items,
    string Reason);

/// <summary>Checks and approval state for a proposed cross-account transfer.</summary>
public sealed record TransferPreview(
    TransferRequest Request,
    bool RequiresApproval,
    IReadOnlyList<string> Checks,
    DateTimeOffset CreatedAt);

/// <summary>Composable application module; CLI, Avalonia and MCP can consume the same runtime.</summary>
public interface IKolibriModule
{
    /// <summary>Stable identifier for this module.</summary>
    string Id { get; }
    /// <summary>Capabilities implemented by this module.</summary>
    IReadOnlySet<KolibriCapability> Capabilities { get; }
}

/// <summary>Inspectable LibKolibri runtime metadata for CLI, UI and MCP hosts.</summary>
public sealed record KolibriRuntimeDescriptor(
    string Name,
    string Version,
    IReadOnlyList<IKolibriModule> Modules);
