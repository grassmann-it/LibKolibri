using KolArbitrage.Core.Adventure;
using KolArbitrage.Core.Integration;

namespace LibKolibri;

/// <summary>
/// Bridges the current Core account read client to the stable LibKolibri
/// account contract. It deliberately uses Core's cache policy and never
/// performs a mutating request.
/// </summary>
public sealed class CoreAccountGateway : IAccountGateway, IKolibriModule
{
    private readonly IAccountReadClient _client;

    /// <summary>Creates an account gateway over an existing Core read client.</summary>
    public CoreAccountGateway(IAccountReadClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <inheritdoc />
    public string Id => "core.account";

    /// <inheritdoc />
    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Account, KolibriCapability.Inventory };

    /// <inheritdoc />
    public Task<KoLStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
        => _client.FetchStatusAsync(cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<KoLInventorySnapshot> GetInventoryAsync(CancellationToken cancellationToken = default)
        => _client.FetchInventoryAsync(cancellationToken: cancellationToken);
}

/// <summary>Bridges Core's cached mall reader to the LibKolibri mall contract.</summary>
public sealed class CoreMallGateway : IMallGateway, IKolibriModule
{
    private readonly IMallReadClient _client;

    /// <summary>Creates a mall gateway over an existing Core read client.</summary>
    public CoreMallGateway(IMallReadClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <inheritdoc />
    public string Id => "core.mall";

    /// <inheritdoc />
    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Mall, KolibriCapability.Pricing };

    /// <inheritdoc />
    public Task<MallSnapshot> SearchAsync(string item, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        return _client.SearchAsync(item, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OwnShopListing>> GetOwnShopAsync(CancellationToken cancellationToken = default)
        => _client.FetchOwnShopAsync(cancellationToken: cancellationToken);
}

/// <summary>Adapts the pure Core daily adventure planner to the shared contract.</summary>
public sealed class CoreAdventureGateway : IAdventureGateway, IKolibriModule
{
    private readonly DailyAdventurePlanner _planner;

    /// <summary>Creates an adventure gateway with the supplied planner policy.</summary>
    public CoreAdventureGateway(DailyAdventurePlanner? planner = null)
        => _planner = planner ?? new DailyAdventurePlanner();

    /// <inheritdoc />
    public string Id => "core.adventure";

    /// <inheritdoc />
    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Adventure };

    /// <inheritdoc />
    public Task<DailyAdventurePlan> PlanAsync(
        AdventureState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(state);
        return Task.FromResult(_planner.Plan(state));
    }
}

/// <summary>Exposes an already-configured approval-bound combat gateway.</summary>
public sealed class CoreCombatGateway : ICombatGateway, IKolibriModule
{
    private readonly ManualCombatGateway _gateway;

    public CoreCombatGateway(ManualCombatGateway gateway)
        => _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

    public string Id => "core.combat";

    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Combat };

    public Task<ManualCombatInspectionResult> InspectAsync(
        string combatId,
        CancellationToken cancellationToken = default)
        => _gateway.InspectAsync(combatId, cancellationToken);

    public Task<ManualCombatPreview> PreviewAsync(
        ManualCombatRequest request,
        CancellationToken cancellationToken = default)
        => _gateway.PreviewAsync(request, cancellationToken);

    public Task<ManualCombatExecutionResult> ExecuteAsync(
        ManualCombatPreview preview,
        string? approvalToken = null,
        CancellationToken cancellationToken = default)
        => _gateway.ExecuteAsync(preview, approvalToken, cancellationToken);
}

/// <summary>Adapts Core's Foreign Bribe ranking planner to LibKolibri.</summary>
public sealed class CoreCoinmasterGateway : ICoinmasterGateway, IKolibriModule
{
    private readonly ForeignBribePlanner _planner;

    /// <summary>Creates a coinmaster gateway over mall and Pricegun readers.</summary>
    public CoreCoinmasterGateway(ForeignBribePlanner planner)
        => _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    /// <inheritdoc />
    public string Id => "core.coinmasters";

    /// <inheritdoc />
    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Coinmasters, KolibriCapability.Pricing };

    /// <inheritdoc />
    public Task<IReadOnlyList<ForeignBribeRanking>> RankForeignBribesAsync(
        IReadOnlyDictionary<string, decimal>? currencyValues = null,
        CancellationToken cancellationToken = default)
        => _planner.RankAsync(currencyValues, cancellationToken: cancellationToken);
}
