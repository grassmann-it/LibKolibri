#pragma warning disable CS1591

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CoreAuditEvent = KolArbitrage.Core.Integration.AuditEvent;
using CoreAuditEventPhase = KolArbitrage.Core.Integration.AuditEventPhase;
using CoreAuditEventQuery = KolArbitrage.Core.Integration.AuditEventQuery;
using CoreAuditEventJournal = KolArbitrage.Core.Integration.IAuditEventJournal;
using CoreItemMaster = KolArbitrage.Core.Integration.ItemMaster;
using CoreItemMasterRecord = KolArbitrage.Core.Integration.ItemMasterRecord;
using CoreItemVariantLink = KolArbitrage.Core.Integration.ItemVariantLink;
using CoreDurableSnapshotStore = KolArbitrage.Core.Integration.IDurableSnapshotStore;
using CoreSnapshotKey = KolArbitrage.Core.Integration.SnapshotKey;
using CoreSnapshotCachePolicy = KolArbitrage.Core.Integration.SnapshotCachePolicy;
using CoreMallReadClient = KolArbitrage.Core.Integration.IMallReadClient;
using CoreMallSnapshot = KolArbitrage.Core.Integration.MallSnapshot;
using CoreOwnShopListing = KolArbitrage.Core.Integration.OwnShopListing;
using CoreMallListing = KolArbitrage.Core.Integration.MallListing;
using CoreShopMutationClient = KolArbitrage.Core.Integration.KoLShopMutationClient;
using CoreShopPriceUpdate = KolArbitrage.Core.Integration.ShopPriceUpdate;
using CoreShopMutationResult = KolArbitrage.Core.Integration.ShopMutationResult;
using CoreShopPriceUpdateStatus = KolArbitrage.Core.Integration.ShopPriceUpdateStatus;
using CorePriceEngine = KolArbitrage.Core.Pricing.PricingEngine;
using CorePriceSummary = KolArbitrage.Core.Pricing.PriceSummary;
using CorePricingRecommendation = KolArbitrage.Core.Pricing.PricingRecommendation;
using CorePriceObservation = KolArbitrage.Core.Pricing.PriceObservation;
using CoreQuoteSide = KolArbitrage.Core.Pricing.QuoteSide;
using CorePriceSource = KolArbitrage.Core.Pricing.PriceSource;
using CoreArbitrageQueryService = KolArbitrage.Core.Integration.CoreArbitrageQueryService;
using CoreArbitrageObservation = KolArbitrage.Core.Integration.ArbitrageObservation;
using CoreArbitrageQuery = KolArbitrage.Core.Integration.ArbitrageQuery;
using CoreArbitrageCoreResult = KolArbitrage.Core.Integration.CoreArbitrageQueryResult;
using CoreArbitrageCoreState = KolArbitrage.Core.Integration.CoreQueryState;
using CoreMallPriceProvider = KolArbitrage.Core.Integration.KoLMallPriceProvider;
using CoreTransferRequest = KolArbitrage.Core.Integration.TransferRequest;
using CoreTransferObservation = KolArbitrage.Core.Integration.TransferObservation;
using CoreTransferPreview = KolArbitrage.Core.Integration.TransferPreview;
using CoreTransferApproval = KolArbitrage.Core.Integration.TransferApproval;
using CoreTransferCommitRequest = KolArbitrage.Core.Integration.TransferCommitRequest;
using CoreTransferCommitResult = KolArbitrage.Core.Integration.TransferCommitResult;
using CoreTransferVerificationRequest = KolArbitrage.Core.Integration.TransferVerificationRequest;
using CoreTransferVerificationResult = KolArbitrage.Core.Integration.TransferVerificationResult;
using CoreTransferApprovalPolicy = KolArbitrage.Core.Integration.TransferApprovalPolicy;
using CoreTransferIdempotencyStore = KolArbitrage.Core.Integration.ITransferIdempotencyStore;
using CoreTransferReservationStore = KolArbitrage.Core.Integration.ITransferReservationStore;
using KolArbitrage.Core.Integration;
using KolArbitrage.Core.Pricing;

namespace LibKolibri;

/// <summary>Adapts the Core pricing engine to the stable LibKolibri contract.</summary>
public sealed class CorePricingGateway : IPricingGateway, IKolibriModule
{
    private readonly CorePriceEngine _engine;
    private readonly TimeProvider _clock;

    public CorePricingGateway(CorePriceEngine engine, TimeProvider? clock = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _clock = clock ?? TimeProvider.System;
    }

    public string Id => "core.pricing";

    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Pricing, KolibriCapability.Mall };

    public async Task<PricingResult> QueryAsync(PricingQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var summary = await _engine.SummarizeAsync(query.ItemId, query.ForceRefresh, cancellationToken)
                .ConfigureAwait(false);
            var recommendation = await _engine.RecommendAsync(
                query.ItemId, query.AcquisitionCost, query.ForceRefresh, cancellationToken).ConfigureAwait(false);
            var observed = summary.AsOf;
            var freshness = summary.IsStale ? SnapshotFreshness.Stale : SnapshotFreshness.Fresh;
            var state = summary.QuoteCount == 0 ? GatewayState.Empty : summary.IsStale ? GatewayState.Stale : GatewayState.Ready;
            return new PricingResult(
                state,
                summary.ItemId,
                summary,
                recommendation,
                new SnapshotState(freshness, observed, source: "core.pricing"),
                summary.QuoteCount == 0
                    ? [new GatewayDiagnostic("NoQuotes", "No usable price observations were available.")]
                    : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new PricingResult(
                GatewayState.Error,
                query.ItemId.Trim(),
                snapshot: new SnapshotState
                {
                    Freshness = SnapshotFreshness.Missing,
                    Source = "core.pricing",
                    ObservedAt = _clock.GetUtcNow(),
                    ErrorCode = exception.GetType().Name,
                    ErrorMessage = exception.Message
                },
                diagnostics: [new GatewayDiagnostic("PricingFailed", exception.Message, IsError: true, Retryable: true)]);
        }
    }
}

/// <summary>
/// Core arbitrage query/preview adapter. It only reads Mall and pricing
/// observations and returns approval-bound actions; no purchase transport is
/// accepted by this type.
/// </summary>
public sealed class CoreArbitrageGateway : IArbitrageGateway, IKolibriModule
{
    private readonly Func<string, CancellationToken, Task<CoreMallSnapshot>> _search;
    private readonly CorePriceEngine? _pricing;
    private readonly CoreItemMaster? _catalog;
    private readonly CoreArbitrageQueryService? _queryService;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _previewLifetime;
    private readonly decimal? _transferCost;
    private readonly decimal? _transferCostPerUnit;

    public CoreArbitrageGateway(
        CoreMallReadClient mall,
        CorePriceEngine? pricing = null,
        CoreItemMaster? catalog = null,
        TimeProvider? clock = null,
        TimeSpan? previewLifetime = null,
        CoreArbitrageQueryService? queryService = null,
        decimal? transferCost = null,
        decimal? transferCostPerUnit = null)
        : this(
            mall is null ? throw new ArgumentNullException(nameof(mall)) :
                (item, cancellationToken) => mall.SearchAsync(item, cancellationToken: cancellationToken),
            pricing, catalog, clock, previewLifetime, queryService, transferCost, transferCostPerUnit)
    {
    }

    public CoreArbitrageGateway(
        IMallGateway mall,
        CorePriceEngine? pricing = null,
        CoreItemMaster? catalog = null,
        TimeProvider? clock = null,
        TimeSpan? previewLifetime = null,
        CoreArbitrageQueryService? queryService = null,
        decimal? transferCost = null,
        decimal? transferCostPerUnit = null)
        : this(
            mall is null ? throw new ArgumentNullException(nameof(mall)) :
                (item, cancellationToken) => mall.SearchAsync(item, cancellationToken),
            pricing, catalog, clock, previewLifetime, queryService, transferCost, transferCostPerUnit)
    {
    }

    private CoreArbitrageGateway(
        Func<string, CancellationToken, Task<CoreMallSnapshot>> search,
        CorePriceEngine? pricing,
        CoreItemMaster? catalog,
        TimeProvider? clock,
        TimeSpan? previewLifetime,
        CoreArbitrageQueryService? queryService,
        decimal? transferCost,
        decimal? transferCostPerUnit)
    {
        _search = search;
        _pricing = pricing;
        _catalog = catalog;
        _queryService = queryService;
        _clock = clock ?? TimeProvider.System;
        _previewLifetime = previewLifetime ?? TimeSpan.FromMinutes(5);
        if (transferCost is < 0) throw new ArgumentOutOfRangeException(nameof(transferCost));
        if (transferCostPerUnit is < 0) throw new ArgumentOutOfRangeException(nameof(transferCostPerUnit));
        _transferCost = transferCost;
        _transferCostPerUnit = transferCostPerUnit;
        if (_previewLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(previewLifetime));
    }

    public string Id => "core.arbitrage";

    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Mall, KolibriCapability.Pricing };

    public async Task<ArbitrageQueryResult> QueryAsync(
        ArbitrageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var requested = query.ItemIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(query.Limit).ToArray();
        if (requested.Length == 0)
        {
            return new ArbitrageQueryResult(
                GatewayState.Empty,
                snapshot: new SnapshotState(SnapshotFreshness.Missing, _clock.GetUtcNow(), "core.arbitrage"),
                diagnostics: [new GatewayDiagnostic("NoItems", "At least one item id is required for an arbitrage query.")]);
        }

        if (_queryService is not null)
            return await QueryWithCoreServiceAsync(query, requested, cancellationToken).ConfigureAwait(false);

        var candidates = new List<ArbitrageCandidate>();
        var diagnostics = new List<GatewayDiagnostic>();
        var observedAt = _clock.GetUtcNow();
        var anyStale = false;
        foreach (var itemId in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CoreMallSnapshot mall;
            try
            {
                mall = await _search(itemId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(new GatewayDiagnostic("MallReadFailed", $"{itemId}: {exception.Message}", true, true));
                continue;
            }

            observedAt = observedAt > mall.FetchedAt ? observedAt : mall.FetchedAt;
            anyStale |= mall.IsStale;
            var listings = mall.Listings
                .Where(x => !query.ExcludedSellerIds.Contains(x.StoreId))
                .OrderBy(x => x.Price)
                .ToArray();
            if (listings.Length == 0) continue;

            CorePriceSummary? summary = null;
            CorePricingRecommendation? recommendation = null;
            if (_pricing is not null)
            {
                try
                {
                    summary = await _pricing.SummarizeAsync(itemId, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    recommendation = await _pricing.RecommendAsync(itemId, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    diagnostics.Add(new GatewayDiagnostic("PricingReadFailed", $"{itemId}: {exception.Message}", false, true));
                }
            }

            var reference = recommendation?.MarketReferencePrice ?? summary?.WeightedMedianSellPrice;
            reference ??= listings.Length > 1 ? listings.Skip(1).First().Price : listings[0].Price;
            var listing = listings[0];
            var tradeability = _catalog?.Find(itemId);
            var tradeable = tradeability is null || tradeability.IsMallTradeable;
            var accountBound = tradeability?.IsAccountBound == true;
            var ask = (decimal)listing.Price;
            var profit = reference - ask;
            var margin = ask <= 0 ? (decimal?)null : profit / ask * 100m;
            var accepted = tradeable && !accountBound && profit > 0m;
            if (query.MinimumMarginPercent is { } minMargin && (margin is null || margin < minMargin)) accepted = false;
            if (query.MinimumNetProfit is { } minProfit && profit < minProfit) accepted = false;
            var reasons = new List<string>();
            if (!tradeable) reasons.Add("not-tradeable");
            if (accountBound) reasons.Add("account-bound");
            if (profit <= 0m) reasons.Add("no-positive-margin");
            if (mall.IsStale) reasons.Add("stale-mall");
            if (query.MinimumMarginPercent is { } threshold && (margin is null || margin < threshold)) reasons.Add("below-margin-threshold");
            if (query.MinimumNetProfit is { } profitThreshold && profit < profitThreshold) reasons.Add("below-profit-threshold");
            var decision = accepted ? ArbitrageDecision.Buy :
                reasons.Contains("stale-mall") ? ArbitrageDecision.Investigate : ArbitrageDecision.Reject;
            if (!query.IncludeRejected && decision == ArbitrageDecision.Reject) continue;
            candidates.Add(new ArbitrageCandidate
            {
                ItemId = itemId,
                DisplayName = tradeability?.Name,
                SellerId = listing.StoreId,
                AvailableQuantity = listing.Quantity,
                ProposedQuantity = Math.Max(1, Math.Min(listing.Quantity, 1)),
                AskPrice = ask,
                ReferencePrice = reference,
                ExpectedNetProfit = profit,
                MarginPercent = margin,
                IsTradeable = tradeable,
                IsAccountBound = accountBound,
                IsStale = mall.IsStale,
                Decision = decision,
                RequiresApproval = accepted,
                ReasonCodes = reasons,
                Reasons = reasons,
                ObservedAt = mall.FetchedAt,
                Snapshot = new SnapshotState(
                    mall.IsStale ? SnapshotFreshness.Stale : SnapshotFreshness.Fresh,
                    mall.FetchedAt,
                    "core.arbitrage")
            });
        }

        var state = candidates.Count == 0 ? GatewayState.Empty : anyStale ? GatewayState.Stale : GatewayState.Ready;
        return new ArbitrageQueryResult(
            state,
            candidates,
            new SnapshotState(anyStale ? SnapshotFreshness.Stale : SnapshotFreshness.Fresh, observedAt, "core.arbitrage"),
            diagnostics);
    }

    private async Task<ArbitrageQueryResult> QueryWithCoreServiceAsync(
        ArbitrageQuery query,
        IReadOnlyCollection<string> requested,
        CancellationToken cancellationToken)
    {
        var observations = new List<CoreArbitrageObservation>();
        var diagnostics = new List<GatewayDiagnostic>();
        foreach (var itemId in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var mall = await _search(itemId, cancellationToken).ConfigureAwait(false);
                var priceSnapshot = new PriceSnapshot(mall);
                var facts = _catalog?.Find(itemId);
                observations.Add(new CoreArbitrageObservation(priceSnapshot)
                {
                    Tradeability = facts is null ? null : new UnderpricedItemTradeability(
                        facts.IsMallTradeable,
                        facts.IsAccountBound)
                    {
                        IsGiftable = facts.IsGiftTransferable,
                        CanPurchase = facts.IsMallTradeable,
                        CanSell = facts.IsTradeable
                    },
                    TransferCost = _transferCost,
                    TransferCostPerUnit = _transferCostPerUnit
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(new GatewayDiagnostic("MallReadFailed", $"{itemId}: {exception.Message}", true, true));
            }
        }

        var coreQuery = new CoreArbitrageQuery
        {
            ItemIds = requested,
            IncludeRejected = query.IncludeRejected,
            Limit = query.Limit,
            Cursor = query.Cursor,
            AsOf = query.AsOf,
            MaximumAge = query.MaximumAge,
            MinimumMarginPercent = query.MinimumMarginPercent,
            MinimumNetProfit = query.MinimumNetProfit,
            ExcludedSellerIds = query.ExcludedSellerIds,
            TransferCost = _transferCost,
            TransferCostPerUnit = _transferCostPerUnit
        };
        var service = _queryService ?? throw new InvalidOperationException("Arbitrage query service is not configured.");
        var result = await service.QueryAsync(observations, coreQuery,
            query.AsOf ?? _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var candidates = result.Candidates.Select(candidate => new ArbitrageCandidate
        {
            ItemId = candidate.ItemId,
            DisplayName = candidate.DisplayName,
            SellerId = candidate.SellerId,
            AvailableQuantity = candidate.AvailableQuantity,
            ProposedQuantity = candidate.ProposedQuantity,
            AskPrice = candidate.AskPrice,
            ReferencePrice = candidate.ReferencePrice,
            ExpectedNetProfit = candidate.ExpectedNetProfit,
            MarginPercent = candidate.MarginPercent,
            LiquidityScore = candidate.LiquidityScore,
            IsTradeable = candidate.IsTradeable,
            IsAccountBound = candidate.IsAccountBound,
            IsStale = candidate.IsStale,
            Decision = candidate.Decision switch
            {
                KolArbitrage.Core.Integration.CoreArbitrageDecision.Buy => ArbitrageDecision.Buy,
                KolArbitrage.Core.Integration.CoreArbitrageDecision.Hold => ArbitrageDecision.Hold,
                KolArbitrage.Core.Integration.CoreArbitrageDecision.Investigate => ArbitrageDecision.Investigate,
                _ => ArbitrageDecision.Reject
            },
            RequiresApproval = candidate.RequiresApproval,
            ReasonCodes = candidate.ReasonCodes,
            Reasons = candidate.Reasons,
            ObservedAt = candidate.ObservedAt,
            Snapshot = new SnapshotState
            {
                Freshness = candidate.Snapshot.Freshness switch
                {
                    KolArbitrage.Core.Integration.SnapshotFreshness.Fresh => SnapshotFreshness.Fresh,
                    KolArbitrage.Core.Integration.SnapshotFreshness.Stale => SnapshotFreshness.Stale,
                    KolArbitrage.Core.Integration.SnapshotFreshness.Expired => SnapshotFreshness.Expired,
                    KolArbitrage.Core.Integration.SnapshotFreshness.Missing => SnapshotFreshness.Missing,
                    _ => SnapshotFreshness.Unknown
                },
                ObservedAt = candidate.Snapshot.ObservedAt,
                Source = candidate.Snapshot.Source,
                ErrorCode = candidate.Snapshot.ErrorCode,
                ErrorMessage = candidate.Snapshot.ErrorMessage
            }
        }).ToArray();
        var state = result.State switch
        {
            CoreArbitrageCoreState.Ready => GatewayState.Ready,
            CoreArbitrageCoreState.Empty => GatewayState.Empty,
            CoreArbitrageCoreState.Stale => GatewayState.Stale,
            CoreArbitrageCoreState.Error => GatewayState.Error,
            _ => GatewayState.Unknown
        };
        var snapshot = new SnapshotState
        {
            Freshness = result.Snapshot.Freshness switch
            {
                KolArbitrage.Core.Integration.SnapshotFreshness.Fresh => SnapshotFreshness.Fresh,
                KolArbitrage.Core.Integration.SnapshotFreshness.Stale => SnapshotFreshness.Stale,
                KolArbitrage.Core.Integration.SnapshotFreshness.Missing => SnapshotFreshness.Missing,
                _ => SnapshotFreshness.Unknown
            },
            ObservedAt = result.Snapshot.ObservedAt,
            Source = result.Snapshot.Source,
            ErrorCode = result.Snapshot.ErrorCode,
            ErrorMessage = result.Snapshot.ErrorMessage
        };
        diagnostics.AddRange(result.Diagnostics.Select(x => new GatewayDiagnostic(x.Code, x.Message, x.IsError, x.Retryable)));
        return new ArbitrageQueryResult(state, candidates, snapshot, diagnostics, result.NextCursor);
    }

    public async Task<ArbitragePreview> PreviewAsync(
        ArbitragePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var actions = request.Actions?.ToArray() ?? Array.Empty<ArbitrageAction>();
        var candidates = request.Candidates?.ToArray() ?? Array.Empty<ArbitrageCandidate>();
        if (actions.Length == 0 && request.Query is not null)
        {
            var result = await QueryAsync(request.Query, cancellationToken).ConfigureAwait(false);
            candidates = result.Candidates.ToArray();
            actions = candidates.Where(x => x.Decision == ArbitrageDecision.Buy && x.AskPrice is not null)
                .Select(x => new ArbitrageAction(x.ItemId, Math.Max(1, x.ProposedQuantity), x.AskPrice!.Value, x.SellerId)
                {
                    AccountId = request.AccountId ?? request.Query.AccountId,
                    ExpectedTotalCost = x.AskPrice.Value * Math.Max(1, x.ProposedQuantity)
                }).ToArray();
        }

        var diagnostics = new List<GatewayDiagnostic>();
        var checks = new List<ExpectedStateCheck>();
        foreach (var action in actions)
        {
            try
            {
                action.Validate();
                checks.Add(new ExpectedStateCheck($"item:{action.ItemId}", "valid", "valid"));
            }
            catch (Exception exception)
            {
                checks.Add(new ExpectedStateCheck($"item:{action.ItemId}", "valid", "invalid", false, exception.Message));
                diagnostics.Add(new GatewayDiagnostic("InvalidAction", exception.Message, true));
            }
        }
        var valid = actions.Length > 0 && checks.All(x => x.Passed);
        var now = _clock.GetUtcNow();
        var state = valid ? GatewayState.ApprovalRequired : GatewayState.Blocked;
        var expected = new ExpectedState(checks, new SnapshotState(SnapshotFreshness.Fresh, now, "core.arbitrage"), Fingerprint(actions));
        return new ArbitragePreview(
            Guid.NewGuid().ToString("N"),
            state,
            actions,
            requiresApproval: valid,
            createdAt: now,
            expiresAt: now + _previewLifetime)
        {
            OperationId = Guid.NewGuid().ToString("N"),
            Candidates = candidates,
            Checks = checks,
            Expected = expected,
            Snapshot = expected.Snapshot,
            Diagnostics = diagnostics
        };
    }

    private static string Fingerprint(IEnumerable<ArbitrageAction> actions)
    {
        var value = string.Join("|", actions.Select(x =>
            string.Join(":", x.ItemId.Trim(), x.SellerId?.Trim() ?? string.Empty,
                x.Quantity.ToString(CultureInfo.InvariantCulture),
                x.ExpectedUnitPrice.ToString(CultureInfo.InvariantCulture))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}

/// <summary>In-memory approval ledger. It records decisions but cannot commit KoL mutations.</summary>
public sealed class CoreApprovalGateway : IApprovalGateway, IKolibriModule
{
    private sealed record Entry(ApprovalRequest Request, ApprovalPreview Preview, string? Token);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;

    public CoreApprovalGateway(TimeProvider? clock = null, TimeSpan? lifetime = null)
    {
        _clock = clock ?? TimeProvider.System;
        _lifetime = lifetime ?? TimeSpan.FromMinutes(5);
        if (_lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
    }

    public string Id => "core.approval.memory";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability>();

    public Task<ApprovalPreview> PreviewAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var now = _clock.GetUtcNow();
        var approvalId = Guid.NewGuid().ToString("N");
        var checks = request.Expected.Checks?.ToArray() ?? Array.Empty<ExpectedStateCheck>();
        var valid = !string.IsNullOrWhiteSpace(request.OperationId)
            && !string.IsNullOrWhiteSpace(request.OperationType)
            && checks.All(x => x.Passed);
        var preview = new ApprovalPreview(approvalId, request.OperationId,
            valid ? ApprovalState.Pending : ApprovalState.Rejected)
        {
            PreviewId = request.PreviewId,
            RequiresApproval = request.RequiresApproval,
            Summary = request.Reason,
            Checks = checks,
            Expected = request.Expected,
            CreatedAt = now,
            ExpiresAt = now + _lifetime,
            Diagnostics = valid
                ? Array.Empty<GatewayDiagnostic>()
                : [new GatewayDiagnostic("InvalidApprovalRequest", "Operation id, operation type and expected checks are required.", true)]
        };
        _entries[approvalId] = new Entry(request, preview, null);
        return Task.FromResult(preview);
    }

    public Task<ApprovalResult> ApproveAsync(ApprovalDecisionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var now = _clock.GetUtcNow();
        if (!_entries.TryGetValue(request.ApprovalId, out var entry))
            return Task.FromResult(new ApprovalResult(request.ApprovalId, ApprovalState.Failed)
            {
                Message = "Approval id was not found.",
                ChangedAt = now,
                Diagnostics = [new GatewayDiagnostic("ApprovalNotFound", "The approval id is unknown.", true)]
            });
        if (entry.Preview.ExpiresAt is { } expires && now >= expires)
            return Task.FromResult(new ApprovalResult(request.ApprovalId, ApprovalState.Expired, entry.Request.OperationId)
            {
                Message = "Approval preview expired.", ChangedAt = now,
                Diagnostics = [new GatewayDiagnostic("ApprovalExpired", "The approval preview has expired.", true)]
            });
        if (!request.Approve)
        {
            var rejected = entry.Preview with { State = ApprovalState.Rejected };
            _entries[request.ApprovalId] = entry with { Preview = rejected };
            return Task.FromResult(new ApprovalResult(request.ApprovalId, ApprovalState.Rejected, entry.Request.OperationId)
            {
                Message = request.Comment, ChangedAt = now
            });
        }

        var token = string.IsNullOrWhiteSpace(request.ApprovalToken) ? Guid.NewGuid().ToString("N") : request.ApprovalToken.Trim();
        var approved = entry.Preview with { State = ApprovalState.Approved };
        _entries[request.ApprovalId] = entry with { Preview = approved, Token = token };
        return Task.FromResult(new ApprovalResult(request.ApprovalId, ApprovalState.Approved, entry.Request.OperationId, token)
        {
            Message = request.Comment, ChangedAt = now
        });
    }

    public Task<ApprovalResult> ExpireAsync(ApprovalExpirationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var now = _clock.GetUtcNow();
        if (!_entries.TryGetValue(request.ApprovalId, out var entry))
            return Task.FromResult(new ApprovalResult(request.ApprovalId, ApprovalState.Failed)
            {
                Message = "Approval id was not found.", ChangedAt = now
            });
        _entries[request.ApprovalId] = entry with { Preview = entry.Preview with { State = ApprovalState.Expired } };
        return Task.FromResult(new ApprovalResult(request.ApprovalId, ApprovalState.Expired, entry.Request.OperationId)
        {
            Message = request.Reason, ChangedAt = now
        });
    }

    public Task<ApprovalResult> CommitAsync(ApprovalCommitRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var now = _clock.GetUtcNow();
        if (!_entries.TryGetValue(request.ApprovalId, out var entry))
            return Task.FromResult(new ApprovalResult(request.ApprovalId, ApprovalState.Failed)
            {
                Message = "Approval id was not found.", ChangedAt = now
            });
        var valid = entry.Preview.State == ApprovalState.Approved
            && string.Equals(entry.Preview.PreviewId, request.PreviewId, StringComparison.Ordinal)
            && string.Equals(entry.Token, request.ApprovalToken, StringComparison.Ordinal)
            && (entry.Preview.ExpiresAt is null || now < entry.Preview.ExpiresAt.Value);
        return Task.FromResult(new ApprovalResult(request.ApprovalId, valid ? ApprovalState.Failed : ApprovalState.Rejected, entry.Request.OperationId)
        {
            Message = valid
                ? "Read-only runtime does not provide a mutation commit transport."
                : "Approval token, preview or expiry did not match.",
            ChangedAt = now,
            Diagnostics = [new GatewayDiagnostic("ReadOnlyCommit", "Approval records cannot submit a live mutation.", true)]
        });
    }

    public Task<ApprovalVerification> VerifyAsync(ApprovalVerificationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var checks = request.Expected.Checks?.ToArray() ?? Array.Empty<ExpectedStateCheck>();
        var verified = checks.All(x => x.Passed)
            && (request.After is null || !request.After.IsStale);
        return Task.FromResult(new ApprovalVerification(request.OperationId,
            verified ? ApprovalState.Verified : ApprovalState.Failed, verified)
        {
            Checks = checks,
            Before = request.Before,
            After = request.After,
            Diagnostics = verified
                ? Array.Empty<GatewayDiagnostic>()
                : [new GatewayDiagnostic("VerificationFailed", "Expected checks or freshness did not pass.", true)]
        });
    }
}

/// <summary>Maps the local JSONL audit journal to the LibKolibri audit contract.</summary>
public sealed class CoreAuditLogGateway : IAuditLogGateway, IKolibriModule
{
    private readonly CoreAuditEventJournal _journal;
    private readonly TimeProvider _clock;

    public CoreAuditLogGateway(CoreAuditEventJournal journal, TimeProvider? clock = null)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _clock = clock ?? TimeProvider.System;
    }

    public string Id => "core.audit";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability>();

    public async Task<AuditAppendResult> AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(auditEvent);
        var mapped = ToCore(auditEvent);
        var appended = await _journal.AppendAsync(mapped, cancellationToken).ConfigureAwait(false);
        return new AuditAppendResult(GatewayState.Ready, appended.EventId, appended.OccurredAt);
    }

    public async Task<AuditLogResult> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var coreQuery = new CoreAuditEventQuery
        {
            OperationId = query.OperationId,
            AccountId = query.AccountId,
            Phase = query.Kind is null ? null : ToCorePhase(query.Kind.Value),
            From = query.From,
            To = query.To,
            Limit = query.Limit,
            Descending = false,
            IsStale = query.IncludeStale ? null : false
        };
        var rows = await _journal.QueryAsync(coreQuery, cancellationToken).ConfigureAwait(false);
        var events = rows.Select(ToContract).ToArray();
        return new AuditLogResult(events.Length == 0 ? GatewayState.Empty : GatewayState.Ready, events, query.Cursor);
    }

    private static CoreAuditEvent ToCore(AuditEvent value)
    {
        var eventId = string.IsNullOrWhiteSpace(value.EventId) ? Guid.NewGuid().ToString("N") : value.EventId;
        var operationId = string.IsNullOrWhiteSpace(value.OperationId) ? eventId : value.OperationId;
        var requestId = string.IsNullOrWhiteSpace(value.RequestId) ? eventId : value.RequestId;
        var sourceId = string.IsNullOrWhiteSpace(value.Source) ? "libkolibri" : value.Source;
        return new CoreAuditEvent(
            eventId,
            value.OccurredAt == default ? DateTimeOffset.UtcNow : value.OccurredAt,
            ToCorePhase(value.Kind),
            operationId,
            sourceId,
            requestId,
            value.Kind.ToString(),
            data: value.Data.ToDictionary(x => x.Key, x => (string?)x.Value, StringComparer.Ordinal),
            accountId: value.AccountId,
            isStale: value.IsStale);
    }

    private static AuditEvent ToContract(CoreAuditEvent value)
        => new()
        {
            EventId = value.EventId,
            Kind = value.Phase switch
            {
                CoreAuditEventPhase.Preview => AuditEventKind.Preview,
                CoreAuditEventPhase.Commit => AuditEventKind.MutationCommitted,
                CoreAuditEventPhase.Verify => AuditEventKind.MutationVerified,
                _ => AuditEventKind.Observation
            },
            OperationId = value.OperationId,
            AccountId = value.AccountId,
            Source = value.SourceId,
            RequestId = value.RequestId,
            OccurredAt = value.OccurredAt,
            Freshness = value.IsStale ? SnapshotFreshness.Stale : SnapshotFreshness.Fresh,
            IsStale = value.IsStale,
            Data = value.Data.ToDictionary(x => x.Key, x => x.Value ?? string.Empty, StringComparer.Ordinal)
        };

    private static CoreAuditEventPhase ToCorePhase(AuditEventKind kind)
        => kind switch
        {
            AuditEventKind.MutationVerified => CoreAuditEventPhase.Verify,
            AuditEventKind.MutationCommitted or AuditEventKind.ApprovalGranted => CoreAuditEventPhase.Commit,
            _ => CoreAuditEventPhase.Preview
        };
}

/// <summary>Adapter over the durable local snapshot store.</summary>
public sealed class CoreSnapshotGateway : ISnapshotGateway, IKolibriModule
{
    private readonly CoreDurableSnapshotStore _store;

    public CoreSnapshotGateway(CoreDurableSnapshotStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Id => "core.snapshots";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability>();
    public CoreDurableSnapshotStore Store => _store;

    public Task<SnapshotEnvelope<T>?> ReadAsync<T>(SnapshotQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _store.ReadAsync<T>(query.Key, query.Policy, query.AsOf, cancellationToken);
    }

    public Task<SnapshotEnvelope<T>> WriteAsync<T>(
        CoreSnapshotKey key,
        T payload,
        DateTimeOffset? fetchedAt = null,
        bool stale = false,
        CancellationToken cancellationToken = default)
        => _store.WriteAsync(key, payload, fetchedAt, stale, cancellationToken: cancellationToken);

    public Task<int> InvalidateAsync(CoreSnapshotKey key, CancellationToken cancellationToken = default)
        => _store.InvalidateAsync(key, cancellationToken);
}

/// <summary>Read-only projection of an imported Core item master.</summary>
public sealed class CoreItemCatalogGateway : IItemCatalogGateway, IKolibriModule
{
    private readonly CoreItemMaster _catalog;
    private readonly string? _version;
    private readonly TimeProvider _clock;

    public CoreItemCatalogGateway(CoreItemMaster catalog, string? version = null, TimeProvider? clock = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _version = version;
        _clock = clock ?? TimeProvider.System;
    }

    public string Id => "core.items";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability>();
    public CoreItemMaster Catalog => _catalog;

    public Task<ItemCatalogResult> QueryAsync(ItemCatalogQuery request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var ids = request.ItemIds.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<CoreItemMasterRecord> rows = _catalog;
        if (ids.Count > 0) rows = rows.Where(x => ids.Contains(x.ItemId));
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            rows = rows.Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.ItemId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.SourceTags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }
        if (request.Tradeable is { } tradeable) rows = rows.Where(x => x.IsMallTradeable == tradeable);
        if (request.Giftable is { } giftable) rows = rows.Where(x => x.IsGiftTransferable == giftable);
        if (request.AccountBound is { } bound) rows = rows.Where(x => x.IsAccountBound == bound);
        var items = rows.Take(request.Limit).Select(ToContract).ToArray();
        var state = items.Length == 0 ? GatewayState.Empty : GatewayState.Ready;
        var result = new ItemCatalogResult(state, items,
            new SnapshotState
            {
                Freshness = SnapshotFreshness.Fresh,
                ObservedAt = _clock.GetUtcNow(),
                Source = "core.items",
                SourceVersion = _version
            },
            nextCursor: null);
        return Task.FromResult(result);
    }

    private ItemCatalogEntry ToContract(CoreItemMasterRecord item)
    {
        var variants = new List<ItemVariantLink>();
        if (!string.IsNullOrWhiteSpace(item.OpenedItemId))
            variants.Add(new ItemVariantLink(item.ItemId, item.OpenedItemId!, "opened"));
        if (!string.IsNullOrWhiteSpace(item.PackagedItemId))
            variants.Add(new ItemVariantLink(item.ItemId, item.PackagedItemId!, "packaged"));
        var tradeability = item.IsAccountBound ? ItemTradeabilityState.AccountBound :
            item.IsGiftTransferable ? ItemTradeabilityState.Giftable :
            item.IsMallTradeable ? ItemTradeabilityState.Tradeable : ItemTradeabilityState.NonTradeable;
        return new ItemCatalogEntry(item.ItemId, item.Name, tradeability)
        {
            Aliases = item.Plural is null ? Array.Empty<string>() : [item.Plural],
            IsTradeable = item.IsMallTradeable,
            IsGiftable = item.IsGiftTransferable,
            IsAccountBound = item.IsAccountBound,
            CanPurchase = item.IsMallTradeable,
            CanSell = item.IsMallTradeable,
            IsPackage = item.IsPackaged,
            CanOpen = item.IsPackaged,
            VariantKind = item.IsPackaged ? ItemVariantState.Packaged : item.IsOpened ? ItemVariantState.Opened : ItemVariantState.None,
            PackagedItemId = item.PackagedItemId,
            OpenedItemId = item.OpenedItemId,
            AutosellPrice = item.AutosellPrice,
            NpcPrice = item.NpcPrice,
            Variants = variants,
            Tags = item.SourceTags,
            CatalogVersion = _version,
            Snapshot = new SnapshotState
            {
                Freshness = SnapshotFreshness.Fresh,
                ObservedAt = _clock.GetUtcNow(),
                Source = "core.items",
                SourceVersion = _version
            }
        };
    }
}

/// <summary>Own-shop mutation adapter that remains preview-only by default.</summary>
public class CoreShopMutationGateway : IShopMutationGateway, IKolibriModule
{
    private sealed record StoredPreview(ShopMutationRequest Request, ShopMutationPreview Preview);
    private readonly Func<CancellationToken, Task<IReadOnlyList<CoreOwnShopListing>>> _ownShop;
    private readonly CoreShopMutationClient? _mutator;
    private readonly bool _allowMutations;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _previewLifetime;
    private readonly ConcurrentDictionary<string, StoredPreview> _previews = new(StringComparer.Ordinal);

    public CoreShopMutationGateway(
        CoreMallReadClient mall,
        CoreShopMutationClient? mutator = null,
        bool allowMutations = false,
        TimeProvider? clock = null,
        TimeSpan? previewLifetime = null)
        : this(
            mall is null ? throw new ArgumentNullException(nameof(mall)) :
                cancellationToken => mall.FetchOwnShopAsync(cancellationToken: cancellationToken),
            mutator, allowMutations, clock, previewLifetime)
    {
    }

    public CoreShopMutationGateway(
        IMallGateway mall,
        CoreShopMutationClient? mutator = null,
        bool allowMutations = false,
        TimeProvider? clock = null,
        TimeSpan? previewLifetime = null)
        : this(
            mall is null ? throw new ArgumentNullException(nameof(mall)) :
                cancellationToken => mall.GetOwnShopAsync(cancellationToken),
            mutator, allowMutations, clock, previewLifetime)
    {
    }

    private CoreShopMutationGateway(
        Func<CancellationToken, Task<IReadOnlyList<CoreOwnShopListing>>> ownShop,
        CoreShopMutationClient? mutator,
        bool allowMutations,
        TimeProvider? clock,
        TimeSpan? previewLifetime)
    {
        _ownShop = ownShop;
        _mutator = mutator;
        _allowMutations = allowMutations;
        _clock = clock ?? TimeProvider.System;
        _previewLifetime = previewLifetime ?? TimeSpan.FromMinutes(5);
        if (_previewLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(previewLifetime));
    }

    public string Id => _allowMutations ? "core.shop-mutation" : "core.shop-mutation.preview";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability> { KolibriCapability.Mall };
    public bool MutationsEnabled => _allowMutations && _mutator is not null;

    public async Task<ShopMutationPreview> PreviewAsync(ShopMutationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var observed = await _ownShop(cancellationToken).ConfigureAwait(false);
        var now = _clock.GetUtcNow();
        var byItem = observed.ToDictionary(x => x.ItemId, StringComparer.OrdinalIgnoreCase);
        var results = request.Changes.Select(change =>
        {
            if (!byItem.TryGetValue(change.ItemId, out var current))
                return new ShopMutationItemResult(change.ItemId, change.ExpectedCurrentPrice, null, change.NewPrice, change.ExpectedPurchaseLimit, null,
                    ShopMutationState.Failed, "Item is not listed in the own shop.");
            var conflict = current.Price != decimal.ToInt64(change.ExpectedCurrentPrice)
                || (change.ExpectedPurchaseLimit is not null && current.PurchaseLimit != change.ExpectedPurchaseLimit);
            if (conflict)
                return new ShopMutationItemResult(change.ItemId, change.ExpectedCurrentPrice, current.Price, change.NewPrice,
                    change.ExpectedPurchaseLimit, current.PurchaseLimit, ShopMutationState.Conflict,
                    "Own-shop price or purchase limit changed since the request was created.");
            return new ShopMutationItemResult(change.ItemId, change.ExpectedCurrentPrice, current.Price, change.NewPrice,
                change.ExpectedPurchaseLimit, current.PurchaseLimit,
                request.RequireApproval ? ShopMutationState.ApprovalRequired : ShopMutationState.Ready,
                current.Price == decimal.ToInt64(change.NewPrice) ? "Already at requested price." : null);
        }).ToArray();
        var hasConflict = results.Any(x => x.State is ShopMutationState.Conflict or ShopMutationState.Failed);
        var state = hasConflict ? ShopMutationState.Conflict : request.RequireApproval ? ShopMutationState.ApprovalRequired : ShopMutationState.Ready;
        var checks = results.Select(x => new ExpectedStateCheck(
            $"shop:{x.ItemId}",
            x.ExpectedCurrentPrice.ToString(CultureInfo.InvariantCulture),
            x.ObservedCurrentPrice?.ToString(CultureInfo.InvariantCulture),
            x.State is not ShopMutationState.Conflict and not ShopMutationState.Failed)).ToArray();
        var snapshot = new SnapshotState(SnapshotFreshness.Fresh, observed.Count == 0 ? now : observed.Max(x => x.ObservedAt), "core.shop");
        var preview = new ShopMutationPreview(Guid.NewGuid().ToString("N"), state, results, request.RequireApproval)
        {
            OperationId = Guid.NewGuid().ToString("N"),
            Expected = new ExpectedState(checks, snapshot, Fingerprint(request)),
            Snapshot = snapshot,
            CreatedAt = now,
            ExpiresAt = now + _previewLifetime,
            Diagnostics = hasConflict ? [new GatewayDiagnostic("ShopConflict", "One or more expected shop values changed.", true)] : Array.Empty<GatewayDiagnostic>()
        };
        _previews[preview.PreviewId] = new StoredPreview(request, preview);
        return preview;
    }

    public async Task<ShopMutationResult> CommitAsync(ShopMutationCommitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_previews.TryGetValue(request.PreviewId, out var stored))
            return FailedResult(request.PreviewId, "Shop preview was not found.");
        var preview = stored.Preview;
        var now = _clock.GetUtcNow();
        if (preview.ExpiresAt is { } expiry && now >= expiry)
            return FailedResult(request.PreviewId, "Shop preview expired.", ShopMutationState.Conflict);
        if (preview.State == ShopMutationState.Conflict)
            return FailedResult(request.PreviewId, "Shop preview contains conflicts.", ShopMutationState.Conflict);
        if (!_allowMutations || _mutator is null)
            return FailedResult(request.PreviewId, "Read-only runtime does not submit shop mutations.", ShopMutationState.Rejected);
        if (string.IsNullOrWhiteSpace(request.ApprovalToken))
            return FailedResult(request.PreviewId, "An approval token is required.", ShopMutationState.Rejected);

        var updates = stored.Request.Changes.Select(change => new CoreShopPriceUpdate(
            change.ItemId,
            checked((long)change.ExpectedCurrentPrice),
            checked((long)change.NewPrice),
            change.ExpectedPurchaseLimit)).ToArray();
        var applied = await _mutator.ApplyPriceUpdatesAsync(updates, commit: true, cancellationToken).ConfigureAwait(false);
        return ToResult(request.PreviewId, applied, now);
    }

    public async Task<ShopMutationResult> VerifyAsync(string previewId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_previews.TryGetValue(previewId, out var stored))
            return FailedResult(previewId, "Shop preview was not found.");
        var observed = await _ownShop(cancellationToken).ConfigureAwait(false);
        var byItem = observed.ToDictionary(x => x.ItemId, StringComparer.OrdinalIgnoreCase);
        var results = stored.Request.Changes.Select(change =>
        {
            if (!byItem.TryGetValue(change.ItemId, out var current))
                return new ShopMutationItemResult(change.ItemId, change.ExpectedCurrentPrice, null, change.NewPrice,
                    change.ExpectedPurchaseLimit, null, ShopMutationState.Failed, "Item disappeared from own shop.");
            var verified = current.Price == decimal.ToInt64(change.NewPrice)
                && (change.NewPurchaseLimit is null || current.PurchaseLimit == change.NewPurchaseLimit);
            return new ShopMutationItemResult(change.ItemId, change.ExpectedCurrentPrice, current.Price, change.NewPrice,
                change.ExpectedPurchaseLimit, current.PurchaseLimit, verified ? ShopMutationState.Verified : ShopMutationState.Conflict,
                verified ? null : "Fresh shop read does not match the requested value.");
        }).ToArray();
        var verifiedAll = results.All(x => x.State == ShopMutationState.Verified);
        return new ShopMutationResult(previewId, verifiedAll ? ShopMutationState.Verified : ShopMutationState.Conflict,
            committed: false, verified: verifiedAll)
        {
            Results = results,
            Snapshot = new SnapshotState(SnapshotFreshness.Fresh, _clock.GetUtcNow(), "core.shop"),
            Diagnostics = verifiedAll ? Array.Empty<GatewayDiagnostic>() : [new GatewayDiagnostic("ShopVerificationFailed", "Own-shop verification did not match the requested values.", true)]
        };
    }

    private static string Fingerprint(ShopMutationRequest request)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", request.Changes.Select(x =>
            $"{x.ItemId}:{x.ExpectedCurrentPrice.ToString(CultureInfo.InvariantCulture)}:{x.NewPrice.ToString(CultureInfo.InvariantCulture)}")))));

    private static ShopMutationResult FailedResult(string previewId, string message, ShopMutationState state = ShopMutationState.Failed)
        => new(previewId, state)
        {
            ObservedAt = DateTimeOffset.UtcNow,
            Diagnostics = [new GatewayDiagnostic("ShopMutationUnavailable", message, true)]
        };

    private static ShopMutationResult ToResult(string previewId, CoreShopMutationResult result, DateTimeOffset now)
    {
        var rows = result.Results.Select(x => new ShopMutationItemResult(
            x.ItemId, x.ExpectedCurrentPrice, x.ObservedCurrentPrice, x.NewPrice,
            x.ExpectedPurchaseLimit, x.ObservedPurchaseLimit,
            x.Status switch
            {
                CoreShopPriceUpdateStatus.Updated => ShopMutationState.Verified,
                CoreShopPriceUpdateStatus.Conflict => ShopMutationState.Conflict,
                CoreShopPriceUpdateStatus.Missing => ShopMutationState.Failed,
                CoreShopPriceUpdateStatus.Hold => ShopMutationState.Ready,
                _ => ShopMutationState.Failed
            }, x.Message)).ToArray();
        var verified = result.Succeeded && rows.All(x => x.State is ShopMutationState.Verified or ShopMutationState.Ready);
        return new ShopMutationResult(previewId,
            verified ? ShopMutationState.Verified : result.Committed ? ShopMutationState.Conflict : ShopMutationState.Failed,
            result.Committed, verified)
        {
            Results = rows,
            ObservedAt = result.ObservedAt,
            Snapshot = new SnapshotState(SnapshotFreshness.Fresh, now, "core.shop"),
            Diagnostics = verified ? Array.Empty<GatewayDiagnostic>() : [new GatewayDiagnostic("ShopMutationFailed", "Shop mutation did not verify.", true)]
        };
    }
}

/// <summary>Explicit name for the safe default shop gateway.</summary>
public sealed class PreviewOnlyShopMutationGateway : CoreShopMutationGateway
{
    public PreviewOnlyShopMutationGateway(
        CoreMallReadClient mall,
        TimeProvider? clock = null,
        TimeSpan? previewLifetime = null)
        : base(mall, mutator: null, allowMutations: false, clock, previewLifetime) { }

    public PreviewOnlyShopMutationGateway(
        IMallGateway mall,
        TimeProvider? clock = null,
        TimeSpan? previewLifetime = null)
        : base(mall, mutator: null, allowMutations: false, clock, previewLifetime) { }
}

/// <summary>Safe result when a host has not configured a Mall read source.</summary>
public sealed class UnconfiguredPricingGateway : IPricingGateway, IKolibriModule
{
    public string Id => "core.pricing.unconfigured";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability> { KolibriCapability.Pricing };
    public Task<PricingResult> QueryAsync(PricingQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PricingResult(
            GatewayState.Error,
            query.ItemId?.Trim() ?? string.Empty,
            snapshot: new SnapshotState
            {
                Freshness = SnapshotFreshness.Missing,
                ObservedAt = DateTimeOffset.UtcNow,
                Source = "core.pricing",
                ErrorCode = "Unconfigured",
                ErrorMessage = "No read-only pricing provider is configured."
            },
            diagnostics: [new GatewayDiagnostic("PricingUnavailable", "Configure a read-only price provider before querying prices.", true)]));
    }
}

/// <summary>Safe result when a host has not configured a Mall read source.</summary>
public sealed class UnconfiguredArbitrageGateway : IArbitrageGateway, IKolibriModule
{
    public string Id => "core.arbitrage.unconfigured";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability> { KolibriCapability.Pricing, KolibriCapability.Mall };
    public Task<ArbitrageQueryResult> QueryAsync(ArbitrageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ArbitrageQueryResult(
            GatewayState.Error,
            snapshot: new SnapshotState(SnapshotFreshness.Missing, DateTimeOffset.UtcNow, "core.arbitrage"),
            diagnostics: [new GatewayDiagnostic("ArbitrageUnavailable", "Configure a read-only Mall source before querying opportunities.", true)]));
    }

    public Task<ArbitragePreview> PreviewAsync(ArbitragePreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new ArbitragePreview(Guid.NewGuid().ToString("N"), GatewayState.Blocked,
            request.Actions, requiresApproval: false, createdAt: now, expiresAt: now)
        {
            Diagnostics = [new GatewayDiagnostic("ArbitrageUnavailable", "No read-only Mall source is configured.", true)]
        });
    }
}

/// <summary>Safe no-op mutation boundary when no own-shop read source exists.</summary>
public sealed class UnconfiguredShopMutationGateway : IShopMutationGateway, IKolibriModule
{
    public string Id => "core.shop-mutation.unconfigured";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability> { KolibriCapability.Mall };
    public Task<ShopMutationPreview> PreviewAsync(ShopMutationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new ShopMutationPreview(Guid.NewGuid().ToString("N"), ShopMutationState.Failed,
            requiresApproval: false)
        {
            CreatedAt = now,
            ExpiresAt = now,
            Diagnostics = [new GatewayDiagnostic("ShopUnavailable", "Configure a read-only own-shop source before previewing changes.", true)]
        });
    }

    public Task<ShopMutationResult> CommitAsync(ShopMutationCommitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ShopMutationResult(request.PreviewId, ShopMutationState.Rejected)
        {
            Diagnostics = [new GatewayDiagnostic("ReadOnlyCommit", "No mutation transport is configured.", true)]
        });
    }

    public Task<ShopMutationResult> VerifyAsync(string previewId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ShopMutationResult(previewId ?? string.Empty, ShopMutationState.Failed)
        {
            Diagnostics = [new GatewayDiagnostic("ShopUnavailable", "No own-shop source is configured.", true)]
        });
    }
}

/// <summary>Process-local audit sink used by the safe default runtime.</summary>
public sealed class InMemoryAuditLogGateway : IAuditLogGateway, IKolibriModule
{
    private readonly object _gate = new();
    private readonly List<AuditEvent> _events = [];
    private readonly TimeProvider _clock;

    public InMemoryAuditLogGateway(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;
    public string Id => "core.audit.memory";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability>();
    public Task<AuditAppendResult> AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();
        var value = string.IsNullOrWhiteSpace(auditEvent.EventId) ? auditEvent with { EventId = Guid.NewGuid().ToString("N") } : auditEvent;
        lock (_gate) _events.Add(value);
        return Task.FromResult(new AuditAppendResult(GatewayState.Ready, value.EventId,
            value.OccurredAt == default ? _clock.GetUtcNow() : value.OccurredAt));
    }

    public Task<AuditLogResult> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        AuditEvent[] rows;
        lock (_gate)
        {
            rows = _events.Where(x => query.OperationId is null || x.OperationId == query.OperationId)
                .Where(x => query.AccountId is null || x.AccountId == query.AccountId)
                .Where(x => query.Kind is null || x.Kind == query.Kind)
                .Where(x => query.From is null || x.OccurredAt >= query.From)
                .Where(x => query.To is null || x.OccurredAt <= query.To)
                .Where(x => query.IncludeStale || !x.IsStale)
                .Take(query.Limit)
                .ToArray();
        }
        return Task.FromResult(new AuditLogResult(rows.Length == 0 ? GatewayState.Empty : GatewayState.Ready, rows));
    }
}

/// <summary>Process-local snapshot cache used when no durable directory is configured.</summary>
public sealed class InMemorySnapshotGateway : ISnapshotGateway, IKolibriModule
{
    private sealed record Entry(object? Payload, Type PayloadType, DateTimeOffset FetchedAt, bool Stale, int Version);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public InMemorySnapshotGateway(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;
    public string Id => "core.snapshots.memory";
    public IReadOnlySet<KolibriCapability> Capabilities { get; } = new HashSet<KolibriCapability>();

    public Task<SnapshotEnvelope<T>?> ReadAsync<T>(SnapshotQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_entries.TryGetValue(Key(query.Key), out var entry) || entry.PayloadType != typeof(T))
            return Task.FromResult<SnapshotEnvelope<T>?>(null);
        var now = query.AsOf ?? _clock.GetUtcNow();
        if (query.Policy is { } policy)
        {
            policy.Validate();
            var age = now - entry.FetchedAt;
            if (age > policy.MaxStale) return Task.FromResult<SnapshotEnvelope<T>?>(null);
            if (age > policy.CacheTtl) entry = entry with { Stale = true };
        }
        return Task.FromResult<SnapshotEnvelope<T>?>(new SnapshotEnvelope<T>(
            entry.Version, query.Key.ItemId, query.Key.AccountId, query.Key.Source,
            entry.FetchedAt, entry.Stale, string.Empty, (T)entry.Payload!));
    }

    public Task<SnapshotEnvelope<T>> WriteAsync<T>(SnapshotKey key, T payload, DateTimeOffset? fetchedAt = null,
        bool stale = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        var at = fetchedAt ?? _clock.GetUtcNow();
        _entries[Key(key)] = new Entry(payload, typeof(T), at, stale, SnapshotEnvelope<T>.CurrentVersion);
        return Task.FromResult(new SnapshotEnvelope<T>(key, payload, at, stale));
    }

    public Task<int> InvalidateAsync(SnapshotKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.TryRemove(Key(key), out _) ? 1 : 0);
    }

    private static string Key(SnapshotKey key)
        => string.Join("\u001f", key.ItemId ?? string.Empty, key.AccountId ?? string.Empty, key.Source);
}

#pragma warning restore CS1591
