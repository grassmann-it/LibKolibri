#pragma warning disable CS1591

using KolArbitrage.Core.Integration;
using KolArbitrage.Core.Pricing;

namespace LibKolibri;

/// <summary>Composition options for LibKolibri's M0/M1 Core adapters.</summary>
public sealed record CoreM0M1RegistrationOptions
{
    /// <summary>Existing read-only Mall adapter. No network client is created by registration.</summary>
    public IMallReadClient? Mall { get; init; }
    /// <summary>Existing LibKolibri Mall gateway, useful for hosts that already wrapped Core.</summary>
    public IMallGateway? MallGateway { get; init; }
    /// <summary>Existing pricing engine. When omitted, one is built from <see cref="PriceProvider"/>.</summary>
    public PricingEngine? PricingEngine { get; init; }
    /// <summary>Read-only source used to build a pricing engine when needed.</summary>
    public IPriceProvider? PriceProvider { get; init; }
    /// <summary>Optional robust, read-only arbitrage query service.</summary>
    public CoreArbitrageQueryService? ArbitrageQueryService { get; init; }
    /// <summary>Explicit transfer route costs for arbitrage previews. Null means unknown.</summary>
    public decimal? ArbitrageTransferCost { get; init; }
    public decimal? ArbitrageTransferCostPerUnit { get; init; }
    /// <summary>Offline item master used by catalog and arbitrage adapters.</summary>
    public ItemMaster? ItemMaster { get; init; }
    /// <summary>Optional durable local snapshot store.</summary>
    public IDurableSnapshotStore? SnapshotStore { get; init; }
    /// <summary>Optional local audit journal.</summary>
    public IAuditEventJournal? AuditJournal { get; init; }
    /// <summary>Creates a JSONL audit journal when <see cref="AuditJournal"/> is omitted.</summary>
    public string? AuditPath { get; init; }
    /// <summary>Creates a durable snapshot store when <see cref="SnapshotStore"/> is omitted.</summary>
    public string? SnapshotDirectory { get; init; }
    public SnapshotCachePolicy? SnapshotPolicy { get; init; }
    public AuditJournalOptions? AuditOptions { get; init; }
    /// <summary>Optional durable/process-local operation approval ledger.</summary>
    public IOperationApprovalLedger? ApprovalLedger { get; init; }
    public IOperationLedgerStore? ApprovalStore { get; init; }
    public OperationLedgerOptions? ApprovalOptions { get; init; }
    public TimeProvider? Clock { get; init; }
    public TimeSpan? PreviewLifetime { get; init; }
    /// <summary>
    /// Explicit opt-in for the live own-shop mutation client. The default is
    /// false even if a mutator is supplied.
    /// </summary>
    public bool EnableShopMutations { get; init; }
    public KoLShopMutationClient? ShopMutator { get; init; }
    public TransferApprovalPolicy? TransferPolicy { get; init; }
    public ITransferIdempotencyStore? TransferIdempotency { get; init; }
    public ITransferReservationStore? TransferReservations { get; init; }
    /// <summary>Optional live transfer transport. It is ignored unless explicitly enabled.</summary>
    public IMainMallbotTransferTransport? TransferTransport { get; init; }
    public MainMallbotTransferAccountBinding? MainAccount { get; init; }
    public MainMallbotTransferAccountBinding? MallbotAccount { get; init; }
    public MainMallbotTransferPolicy? TransferWorkflowPolicy { get; init; }
    public bool EnableTransferMutations { get; init; }
}

/// <summary>
/// Registers the Core-backed M0/M1 gateways. Registration is additive and
/// never creates a live mutation transport unless the caller explicitly opts
/// in through <see cref="CoreM0M1RegistrationOptions.EnableShopMutations"/>.
/// </summary>
public static class CoreM0M1Registration
{
    public static LibKolibriRuntime RegisterCoreM0M1Adapters(
        this LibKolibriRuntime runtime,
        CoreM0M1RegistrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        options ??= new CoreM0M1RegistrationOptions();
        var clock = options.Clock ?? TimeProvider.System;

        // Approval and transfer gateways are intentionally local/preview-only
        // and can therefore be registered without any HTTP dependencies.
        var approval = new CoreApprovalGateway(clock, options.PreviewLifetime);
        runtime.Register<IApprovalGateway>(approval).Register(approval);
        var approvalLedger = options.ApprovalLedger ?? new OperationLedgerService(
            options.ApprovalStore, clock, options.ApprovalOptions);
        runtime.Register<IOperationApprovalLedger>(approvalLedger).Register(approvalLedger);
        if (approvalLedger is IOperationLedger operationLedger)
            runtime.Register<IOperationLedger>(operationLedger);

        var transferPolicy = options.TransferPolicy ?? new TransferApprovalPolicy();
        var transfer = new PreviewOnlyMainMallbotTransferGateway(
            transferPolicy,
            clock,
            options.TransferIdempotency,
            options.TransferReservations);
        runtime.Register<IMainMallbotTransferGateway>(transfer)
            .Register<ITransferWorkflowGateway>(transfer)
            .Register(transfer);
        if (options.EnableTransferMutations
            && options.TransferTransport is not null
            && options.MainAccount is not null
            && options.MallbotAccount is not null)
        {
            var liveTransfer = new CoreMainMallbotTransferGateway(
                options.TransferTransport,
                options.MainAccount,
                options.MallbotAccount,
                options.ItemMaster,
                options.TransferWorkflowPolicy,
                transferPolicy,
                options.TransferIdempotency,
                options.TransferReservations,
                clock);
            runtime.Register<IMainMallbotTransferGateway>(liveTransfer)
                .Register<ITransferWorkflowGateway>(liveTransfer)
                .Register(liveTransfer);
        }
        var simpleTransfer = new PreviewOnlyTransferGateway(clock);
        runtime.Register<ITransferGateway>(simpleTransfer).Register(simpleTransfer);

        var pricing = options.PricingEngine;
        if (pricing is null && options.PriceProvider is not null)
            pricing = new PricingEngine(options.PriceProvider, clock: clock);
        else if (pricing is null && options.Mall is not null)
            pricing = new PricingEngine(new KoLMallPriceProvider(options.Mall), clock: clock);
        if (pricing is not null)
        {
            var pricingGateway = new CorePricingGateway(pricing, clock);
            runtime.Register<IPricingGateway>(pricingGateway).Register(pricingGateway).Register(pricing);
        }
        else
        {
            var pricingGateway = new UnconfiguredPricingGateway();
            runtime.Register<IPricingGateway>(pricingGateway).Register(pricingGateway);
        }

        if (options.ArbitrageQueryService is not null)
            runtime.Register(options.ArbitrageQueryService);

        var mallGateway = options.MallGateway;
        if (mallGateway is null && options.Mall is not null)
        {
            mallGateway = new CoreMallGateway(options.Mall);
            runtime.Register<IMallGateway>(mallGateway).Register(mallGateway);
        }

        if (options.ItemMaster is not null)
        {
            var catalog = new CoreItemCatalogGateway(options.ItemMaster, clock: clock);
            runtime.Register<IItemCatalogGateway>(catalog).Register(catalog).Register(options.ItemMaster);
        }

        if (mallGateway is not null)
        {
            var arbitrage = new CoreArbitrageGateway(
                mallGateway,
                pricing,
                options.ItemMaster,
                clock,
                options.PreviewLifetime,
                options.ArbitrageQueryService,
                options.ArbitrageTransferCost,
                options.ArbitrageTransferCostPerUnit);
            runtime.Register<IArbitrageGateway>(arbitrage).Register(arbitrage);

            CoreShopMutationGateway shop;
            if (options.EnableShopMutations && options.ShopMutator is not null)
            {
                shop = new CoreShopMutationGateway(
                    mallGateway,
                    options.ShopMutator,
                    allowMutations: true,
                    clock,
                    options.PreviewLifetime);
            }
            else
            {
                shop = new PreviewOnlyShopMutationGateway(mallGateway, clock, options.PreviewLifetime);
            }
            runtime.Register<IShopMutationGateway>(shop).Register(shop);
        }
        else
        {
            var arbitrage = new UnconfiguredArbitrageGateway();
            var shop = new UnconfiguredShopMutationGateway();
            runtime.Register<IArbitrageGateway>(arbitrage).Register(arbitrage)
                .Register<IShopMutationGateway>(shop).Register(shop);
        }

        if (options.SnapshotStore is not null || options.SnapshotDirectory is not null)
        {
            var store = options.SnapshotStore ?? new DurableSnapshotStore(
                options.SnapshotDirectory!, options.SnapshotPolicy, clock);
            var snapshots = new CoreSnapshotGateway(store);
            runtime.Register<ISnapshotGateway>(snapshots).Register(snapshots).Register(store);
        }
        else
        {
            var snapshots = new InMemorySnapshotGateway(clock);
            runtime.Register<ISnapshotGateway>(snapshots).Register(snapshots);
        }

        if (options.AuditJournal is not null || options.AuditPath is not null)
        {
            var journal = options.AuditJournal ?? new AuditEventJournal(options.AuditPath, options.AuditOptions);
            var audit = new CoreAuditLogGateway(journal, clock);
            runtime.Register<IAuditLogGateway>(audit).Register(audit).Register(journal);
        }
        else
        {
            var audit = new InMemoryAuditLogGateway(clock);
            runtime.Register<IAuditLogGateway>(audit).Register(audit);
        }

        return runtime;
    }

    /// <summary>Compatibility alias used by hosts that call the adapters workflows.</summary>
    public static LibKolibriRuntime RegisterCoreWorkflowAdapters(
        this LibKolibriRuntime runtime,
        CoreM0M1RegistrationOptions? options = null)
        => RegisterCoreM0M1Adapters(runtime, options);

    /// <summary>Compatibility alias emphasizing that the defaults are read-only.</summary>
    public static LibKolibriRuntime RegisterCoreReadOnlyM0M1Adapters(
        this LibKolibriRuntime runtime,
        CoreM0M1RegistrationOptions? options = null)
        => RegisterCoreM0M1Adapters(runtime, options is null
            ? new CoreM0M1RegistrationOptions()
            : new CoreM0M1RegistrationOptions
            {
                Mall = options.Mall,
                MallGateway = options.MallGateway,
                PricingEngine = options.PricingEngine,
                PriceProvider = options.PriceProvider,
                ArbitrageQueryService = options.ArbitrageQueryService,
                ArbitrageTransferCost = options.ArbitrageTransferCost,
                ArbitrageTransferCostPerUnit = options.ArbitrageTransferCostPerUnit,
                ItemMaster = options.ItemMaster,
                SnapshotStore = options.SnapshotStore,
                AuditJournal = options.AuditJournal,
                AuditPath = options.AuditPath,
                SnapshotDirectory = options.SnapshotDirectory,
                SnapshotPolicy = options.SnapshotPolicy,
                AuditOptions = options.AuditOptions,
                ApprovalLedger = options.ApprovalLedger,
                ApprovalStore = options.ApprovalStore,
                ApprovalOptions = options.ApprovalOptions,
                Clock = options.Clock,
                PreviewLifetime = options.PreviewLifetime,
                TransferPolicy = options.TransferPolicy,
                TransferIdempotency = options.TransferIdempotency,
                TransferReservations = options.TransferReservations
            });

    /// <summary>Builds a fresh runtime populated with safe local/read adapters.</summary>
    public static LibKolibriRuntime CreateCoreSafeRuntime(CoreM0M1RegistrationOptions? options = null)
        => new LibKolibriRuntime().RegisterCoreM0M1Adapters(options);

    /// <summary>Positional convenience overload for common Mall/pricing hosts.</summary>
    public static LibKolibriRuntime RegisterCoreM0M1Adapters(
        this LibKolibriRuntime runtime,
        PricingEngine pricing,
        IMallReadClient mall,
        ItemMaster? itemMaster = null,
        IDurableSnapshotStore? snapshots = null,
        IAuditEventJournal? audit = null,
        TimeProvider? clock = null)
        => RegisterCoreM0M1Adapters(runtime, new CoreM0M1RegistrationOptions
        {
            PricingEngine = pricing,
            Mall = mall,
            ItemMaster = itemMaster,
            SnapshotStore = snapshots,
            AuditJournal = audit,
            Clock = clock
        });
}

#pragma warning restore CS1591
