#pragma warning disable CS1591
namespace LibKolibri;

/// <summary>
/// Common state values returned by the M0/M1 query and preview contracts.
/// A state is part of the payload so a caller never has to infer stale or
/// approval-required data from a missing value.
/// </summary>
public enum GatewayState
{
    Unknown,
    Loading,
    Ready,
    Empty,
    Stale,
    Blocked,
    ApprovalRequired,
    Approved,
    Rejected,
    Expired,
    Conflict,
    Committed,
    Verified,
    Error
}

/// <summary>Read-side state alias for hosts that distinguish queries from mutations.</summary>
public enum QueryState
{
    Unknown,
    Loading,
    Ready,
    Empty,
    Stale,
    Error
}

/// <summary>Preview-side state alias for hosts that use a dedicated status type.</summary>
public enum PreviewState
{
    Unknown,
    Ready,
    ApprovalRequired,
    Blocked,
    Conflict,
    Expired,
    Rejected,
    Committed,
    Verified,
    Error
}

internal static class ContractStateMapping
{
    public static GatewayState ToGateway(this QueryState state) => state switch
    {
        QueryState.Loading => GatewayState.Loading,
        QueryState.Ready => GatewayState.Ready,
        QueryState.Empty => GatewayState.Empty,
        QueryState.Stale => GatewayState.Stale,
        QueryState.Error => GatewayState.Error,
        _ => GatewayState.Unknown
    };

    public static GatewayState ToGateway(this PreviewState state) => state switch
    {
        PreviewState.Ready => GatewayState.Ready,
        PreviewState.ApprovalRequired => GatewayState.ApprovalRequired,
        PreviewState.Blocked => GatewayState.Blocked,
        PreviewState.Conflict => GatewayState.Conflict,
        PreviewState.Expired => GatewayState.Expired,
        PreviewState.Rejected => GatewayState.Rejected,
        PreviewState.Committed => GatewayState.Committed,
        PreviewState.Verified => GatewayState.Verified,
        PreviewState.Error => GatewayState.Error,
        _ => GatewayState.Unknown
    };

    public static QueryState ToQuery(this GatewayState state) => state switch
    {
        GatewayState.Loading => QueryState.Loading,
        GatewayState.Ready => QueryState.Ready,
        GatewayState.Empty => QueryState.Empty,
        GatewayState.Stale => QueryState.Stale,
        GatewayState.Error => QueryState.Error,
        _ => QueryState.Unknown
    };

    public static PreviewState ToPreview(this GatewayState state) => state switch
    {
        GatewayState.Ready => PreviewState.Ready,
        GatewayState.ApprovalRequired => PreviewState.ApprovalRequired,
        GatewayState.Blocked => PreviewState.Blocked,
        GatewayState.Conflict => PreviewState.Conflict,
        GatewayState.Expired => PreviewState.Expired,
        GatewayState.Rejected => PreviewState.Rejected,
        GatewayState.Committed => PreviewState.Committed,
        GatewayState.Verified => PreviewState.Verified,
        GatewayState.Error => PreviewState.Error,
        _ => PreviewState.Unknown
    };
}

/// <summary>Freshness of an observation used by a query or preview.</summary>
public enum SnapshotFreshness
{
    Unknown,
    Fresh,
    Stale,
    Expired,
    Missing
}

/// <summary>Decision emitted by the arbitrage planner.</summary>
public enum ArbitrageDecision
{
    Unknown,
    Buy,
    Hold,
    Reject,
    Investigate
}

/// <summary>Lifecycle state of an approval or approval-bound operation.</summary>
public enum ApprovalState
{
    Unknown,
    Pending,
    Approved,
    Rejected,
    Expired,
    Committed,
    Verified,
    Failed
}

/// <summary>Tradeability facts supplied by an item catalog.</summary>
public enum ItemTradeabilityState
{
    Unknown,
    Tradeable,
    NonTradeable,
    AccountBound,
    Giftable,
    NotGiftable
}

/// <summary>Known packaged/opened variants without exposing item-master parser details.</summary>
public enum ItemVariantState
{
    None,
    Packaged,
    Opened
}

/// <summary>State of a proposed or committed own-shop mutation.</summary>
public enum ShopMutationState
{
    Unknown,
    Preview,
    Ready,
    ApprovalRequired,
    Committed,
    Verified,
    Conflict,
    Rejected,
    Failed
}

/// <summary>Kind of an immutable event in the audit stream.</summary>
public enum AuditEventKind
{
    Observation,
    Query,
    Preview,
    ApprovalRequested,
    ApprovalGranted,
    ApprovalRejected,
    MutationCommitted,
    MutationVerified,
    MutationFailed,
    Error
}

/// <summary>A source/cache marker attached to every read-side result.</summary>
public sealed record SnapshotState
{
    public SnapshotFreshness Freshness { get; init; } = SnapshotFreshness.Unknown;
    public string? Source { get; init; }
    public string? SourceVersion { get; init; }
    public string? RequestId { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Whether the value may be used only as a marked fallback.</summary>
    public bool IsStale => Freshness is SnapshotFreshness.Stale or SnapshotFreshness.Expired;
    /// <summary>Whether the observation is within its declared freshness window.</summary>
    public bool IsFresh => Freshness == SnapshotFreshness.Fresh;
    /// <summary>Whether this state is usable as a current value.</summary>
    public bool IsUsable => Freshness is SnapshotFreshness.Fresh or SnapshotFreshness.Stale;

    public SnapshotState() { }

    public SnapshotState(
        SnapshotFreshness freshness,
        DateTimeOffset observedAt,
        string? source = null,
        DateTimeOffset? expiresAt = null,
        string? requestId = null)
    {
        Freshness = freshness;
        ObservedAt = observedAt;
        Source = source;
        ExpiresAt = expiresAt;
        RequestId = requestId;
    }
}

/// <summary>A serializable diagnostic that can be shown by CLI, MCP or UI.</summary>
public sealed record GatewayDiagnostic(
    string Code,
    string Message,
    bool IsError = false,
    bool Retryable = false);

/// <summary>A failed check retained in a preview instead of thrown away.</summary>
public sealed record ExpectedStateCheck(
    string Name,
    string ExpectedValue,
    string? ObservedValue = null,
    bool Passed = true,
    string? Message = null);

/// <summary>Expected values and freshness used to bind a future commit.</summary>
public sealed record ExpectedState
{
    public IReadOnlyList<ExpectedStateCheck> Checks { get; init; } = Array.Empty<ExpectedStateCheck>();
    public SnapshotState Snapshot { get; init; } = new();
    public string? Fingerprint { get; init; }

    public ExpectedState() { }

    public ExpectedState(
        IEnumerable<ExpectedStateCheck>? checks,
        SnapshotState? snapshot = null,
        string? fingerprint = null)
    {
        Checks = checks?.ToArray() ?? Array.Empty<ExpectedStateCheck>();
        Snapshot = snapshot ?? new SnapshotState();
        Fingerprint = fingerprint;
    }
}

/// <summary>Bounded arbitrage read request. Item ids are preferred over names.</summary>
public record ArbitrageQuery
{
    public string? AccountId { get; init; }
    public string? WatchlistId { get; init; }
    public IReadOnlyCollection<string> ItemIds { get; init; } = Array.Empty<string>();
    public IReadOnlySet<string> ExcludedSellerIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool IncludeRejected { get; init; }
    public int Limit { get; init; } = 100;
    public string? Cursor { get; init; }
    public DateTimeOffset? AsOf { get; init; }
    public TimeSpan? MaximumAge { get; init; }
    public decimal? MinimumMarginPercent { get; init; }
    public decimal? MinimumNetProfit { get; init; }
    public decimal? MinMarginPercent { get => MinimumMarginPercent; init => MinimumMarginPercent = value; }
    public decimal? MinMargin { get => MinimumMarginPercent; init => MinimumMarginPercent = value; }
    public decimal? MinimumProfit { get => MinimumNetProfit; init => MinimumNetProfit = value; }
    public int MaxResults { get => Limit; init => Limit = value; }
    public string? ItemId
    {
        get => ItemIds.Count == 1 ? ItemIds.First() : null;
        init => ItemIds = string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };
    }

    public void Validate()
    {
        if (Limit <= 0) throw new ArgumentOutOfRangeException(nameof(Limit));
        if (MaximumAge is { } age && age <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MaximumAge));
        if (MinimumMarginPercent is < 0) throw new ArgumentOutOfRangeException(nameof(MinimumMarginPercent));
        if (MinimumNetProfit is < 0) throw new ArgumentOutOfRangeException(nameof(MinimumNetProfit));
    }

    public ArbitrageQuery() { }

    public ArbitrageQuery(string? accountId, IEnumerable<string>? itemIds = null, int limit = 100)
    {
        AccountId = accountId;
        ItemIds = itemIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            ?? Array.Empty<string>();
        Limit = limit;
    }
}

/// <summary>Compatibility name for hosts that call a query a request.</summary>
public record ArbitrageQueryRequest : ArbitrageQuery
{
    public ArbitrageQueryRequest() { }
    public ArbitrageQueryRequest(string? accountId, IEnumerable<string>? itemIds = null, int limit = 100)
        : base(accountId, itemIds, limit) { }
}

/// <summary>A single explainable arbitrage candidate.</summary>
public record ArbitrageCandidate
{
    public string ItemId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? SellerId { get; init; }
    public int AvailableQuantity { get; init; }
    public int ProposedQuantity { get; init; }
    public decimal? AskPrice { get; init; }
    public decimal? ReferencePrice { get; init; }
    public decimal? ExpectedNetProfit { get; init; }
    public decimal? MarginPercent { get; init; }
    public decimal? LiquidityScore { get; init; }
    public bool IsTradeable { get; init; }
    public bool IsAccountBound { get; init; }
    public bool IsStale { get; init; }
    public ArbitrageDecision Decision { get; init; } = ArbitrageDecision.Unknown;
    public bool RequiresApproval { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public DateTimeOffset ObservedAt { get; init; }
    public SnapshotState Snapshot { get; init; } = new();

    public decimal? NetProfit => ExpectedNetProfit;
    public decimal? MarketPrice => ReferencePrice;
    public int Quantity => ProposedQuantity > 0 ? ProposedQuantity : AvailableQuantity;
    public bool IsAccepted => Decision == ArbitrageDecision.Buy;

    public ArbitrageCandidate() { }

    public ArbitrageCandidate(
        string itemId,
        decimal? askPrice,
        decimal? referencePrice,
        decimal? expectedNetProfit = null,
        decimal? marginPercent = null,
        ArbitrageDecision decision = ArbitrageDecision.Unknown)
    {
        ItemId = itemId;
        AskPrice = askPrice;
        ReferencePrice = referencePrice;
        ExpectedNetProfit = expectedNetProfit;
        MarginPercent = marginPercent;
        Decision = decision;
    }
}

/// <summary>Result of a deterministic arbitrage query.</summary>
public record ArbitrageQueryResult
{
    public GatewayState State { get; init; } = GatewayState.Unknown;
    public IReadOnlyList<ArbitrageCandidate> Candidates { get; init; } = Array.Empty<ArbitrageCandidate>();
    public IReadOnlyList<ArbitrageCandidate> Opportunities => Candidates;
    public SnapshotState Snapshot { get; init; } = new();
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();
    public string? NextCursor { get; init; }
    public bool IsStale => Snapshot.IsStale || State == GatewayState.Stale;
    public bool Succeeded => State is GatewayState.Ready or GatewayState.Empty or GatewayState.Stale;
    public QueryState Status => State.ToQuery();

    public ArbitrageQueryResult() { }

    public ArbitrageQueryResult(QueryState state,
        IEnumerable<ArbitrageCandidate>? candidates = null,
        SnapshotState? snapshot = null,
        IEnumerable<GatewayDiagnostic>? diagnostics = null,
        string? nextCursor = null)
        : this(state.ToGateway(), candidates, snapshot, diagnostics, nextCursor) { }

    public ArbitrageQueryResult(
        GatewayState state,
        IEnumerable<ArbitrageCandidate>? candidates = null,
        SnapshotState? snapshot = null,
        IEnumerable<GatewayDiagnostic>? diagnostics = null,
        string? nextCursor = null)
    {
        State = state;
        Candidates = candidates?.ToArray() ?? Array.Empty<ArbitrageCandidate>();
        Snapshot = snapshot ?? new SnapshotState();
        Diagnostics = diagnostics?.ToArray() ?? Array.Empty<GatewayDiagnostic>();
        NextCursor = nextCursor;
    }
}

/// <summary>A bounded action selected for an arbitrage preview.</summary>
public record ArbitrageAction
{
    public string ItemId { get; init; } = string.Empty;
    public string? SellerId { get; init; }
    public int Quantity { get; init; }
    public decimal ExpectedUnitPrice { get; init; }
    public decimal? ExpectedTotalCost { get; init; }
    public string? AccountId { get; init; }
    public string? IdempotencyKey { get; init; }

    public ArbitrageAction() { }

    public ArbitrageAction(string itemId, int quantity, decimal expectedUnitPrice, string? sellerId = null)
    {
        ItemId = itemId;
        Quantity = quantity;
        ExpectedUnitPrice = expectedUnitPrice;
        SellerId = sellerId;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ItemId)) throw new ArgumentException("Item id is required.", nameof(ItemId));
        if (Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(Quantity));
        if (ExpectedUnitPrice < 0) throw new ArgumentOutOfRangeException(nameof(ExpectedUnitPrice));
    }
}

/// <summary>Request for a preview; creating one must not mutate KoL state.</summary>
public record ArbitragePreviewRequest
{
    public ArbitrageQuery? Query { get; init; }
    public IReadOnlyList<ArbitrageAction> Actions { get; init; } = Array.Empty<ArbitrageAction>();
    public IReadOnlyList<ArbitrageCandidate> Candidates { get; init; } = Array.Empty<ArbitrageCandidate>();
    public string? AccountId { get; init; }
    public string? Reason { get; init; }
    public decimal? Budget { get; init; }
    public decimal? Reserve { get; init; }
    public string? IdempotencyKey { get; init; }

    public ArbitragePreviewRequest() { }

    public ArbitragePreviewRequest(ArbitrageQuery query, string? reason = null)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        AccountId = query.AccountId;
        Reason = reason;
    }

    public ArbitragePreviewRequest(IEnumerable<ArbitrageAction> actions, string? reason = null)
    {
        Actions = actions?.ToArray() ?? throw new ArgumentNullException(nameof(actions));
        Reason = reason;
    }
}

/// <summary>Approval-bound, preview-only arbitrage result.</summary>
public record ArbitragePreview
{
    public string PreviewId { get; init; } = string.Empty;
    public string? OperationId { get; init; }
    public GatewayState State { get; init; } = GatewayState.Unknown;
    public bool RequiresApproval { get; init; }
    public IReadOnlyList<ArbitrageAction> Actions { get; init; } = Array.Empty<ArbitrageAction>();
    public IReadOnlyList<ArbitrageCandidate> Candidates { get; init; } = Array.Empty<ArbitrageCandidate>();
    public IReadOnlyList<ArbitrageAction> ProposedActions => Actions;
    public IReadOnlyList<ExpectedStateCheck> Checks { get; init; } = Array.Empty<ExpectedStateCheck>();
    public ExpectedState Expected { get; init; } = new();
    public SnapshotState Snapshot { get; init; } = new();
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && now >= expiry;
    public PreviewState Status => State.ToPreview();

    public ArbitragePreview() { }

    public ArbitragePreview(string previewId, PreviewState state,
        IEnumerable<ArbitrageAction>? actions = null,
        bool requiresApproval = true,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null)
        : this(previewId, state.ToGateway(), actions, requiresApproval, createdAt, expiresAt) { }

    public ArbitragePreview(
        string previewId,
        GatewayState state,
        IEnumerable<ArbitrageAction>? actions = null,
        bool requiresApproval = true,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null)
    {
        PreviewId = previewId;
        State = state;
        Actions = actions?.ToArray() ?? Array.Empty<ArbitrageAction>();
        RequiresApproval = requiresApproval;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        ExpiresAt = expiresAt;
    }
}

/// <summary>A request to create an approval record for a preview.</summary>
public record ApprovalRequest
{
    public string OperationId { get; init; } = string.Empty;
    public string OperationType { get; init; } = string.Empty;
    public string? PreviewId { get; init; }
    public string? AccountId { get; init; }
    public string? RequestedBy { get; init; }
    public string? Reason { get; init; }
    public ExpectedState Expected { get; init; } = new();
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool RequiresApproval { get; init; } = true;

    public ApprovalRequest() { }

    public ApprovalRequest(string operationId, string operationType, string? reason = null)
    {
        OperationId = operationId;
        OperationType = operationType;
        Reason = reason;
        RequestedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Compatibility name for hosts that use a separate preview request type.</summary>
public record ApprovalPreviewRequest : ApprovalRequest
{
    public ApprovalPreviewRequest() { }
    public ApprovalPreviewRequest(string operationId, string operationType, string? reason = null)
        : base(operationId, operationType, reason) { }
}

/// <summary>Human-review representation of an approval request.</summary>
public record ApprovalPreview
{
    public string ApprovalId { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public string? PreviewId { get; init; }
    public ApprovalState State { get; init; } = ApprovalState.Unknown;
    public bool RequiresApproval { get; init; } = true;
    public string? Summary { get; init; }
    public IReadOnlyList<ExpectedStateCheck> Checks { get; init; } = Array.Empty<ExpectedStateCheck>();
    public ExpectedState Expected { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();

    public ApprovalPreview() { }

    public ApprovalPreview(string approvalId, string operationId, ApprovalState state = ApprovalState.Pending)
    {
        ApprovalId = approvalId;
        OperationId = operationId;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Human decision submitted after a preview was displayed.</summary>
public record ApprovalDecisionRequest
{
    public string ApprovalId { get; init; } = string.Empty;
    public string? PreviewId { get; init; }
    public bool Approve { get; init; }
    public string? ApprovalToken { get; init; }
    public string? DecidedBy { get; init; }
    public string? Comment { get; init; }
    public DateTimeOffset DecidedAt { get; init; }

    public ApprovalDecisionRequest() { }

    public ApprovalDecisionRequest(string approvalId, bool approve, string? decidedBy = null, string? comment = null)
    {
        ApprovalId = approvalId;
        Approve = approve;
        DecidedBy = decidedBy;
        Comment = comment;
        DecidedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Request used to mark a pending approval as expired.</summary>
public record ApprovalExpirationRequest(string ApprovalId, string? Reason = null);

/// <summary>Opaque approval token and state returned by an approval operation.</summary>
public record ApprovalResult
{
    public string ApprovalId { get; init; } = string.Empty;
    public string? OperationId { get; init; }
    public ApprovalState State { get; init; } = ApprovalState.Unknown;
    public string? ApprovalToken { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset ChangedAt { get; init; }
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();

    public ApprovalResult() { }

    public ApprovalResult(string approvalId, ApprovalState state, string? operationId = null, string? approvalToken = null)
    {
        ApprovalId = approvalId;
        State = state;
        OperationId = operationId;
        ApprovalToken = approvalToken;
        ChangedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Commit request that binds an approved preview to its expected state.</summary>
public record ApprovalCommitRequest
{
    public string ApprovalId { get; init; } = string.Empty;
    public string PreviewId { get; init; } = string.Empty;
    public string ApprovalToken { get; init; } = string.Empty;
    public string? IdempotencyKey { get; init; }
    public ExpectedState? Expected { get; init; }

    public ApprovalCommitRequest() { }

    public ApprovalCommitRequest(string approvalId, string previewId, string approvalToken)
    {
        ApprovalId = approvalId;
        PreviewId = previewId;
        ApprovalToken = approvalToken;
    }
}

/// <summary>Request for a post-commit verification read.</summary>
public record ApprovalVerificationRequest(
    string OperationId,
    ExpectedState Expected,
    SnapshotState? Before = null,
    SnapshotState? After = null);

/// <summary>Verification status for an approval-bound mutation.</summary>
public record ApprovalVerification
{
    public string OperationId { get; init; } = string.Empty;
    public ApprovalState State { get; init; } = ApprovalState.Unknown;
    public bool Verified { get; init; }
    public IReadOnlyList<ExpectedStateCheck> Checks { get; init; } = Array.Empty<ExpectedStateCheck>();
    public SnapshotState? Before { get; init; }
    public SnapshotState? After { get; init; }
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();

    public ApprovalVerification() { }

    public ApprovalVerification(string operationId, ApprovalState state, bool verified)
    {
        OperationId = operationId;
        State = state;
        Verified = verified;
    }
}

/// <summary>Approval lifecycle; implementations may support only preview in M0/M1.</summary>
public interface IApprovalGateway
{
    /// <summary>Creates a reviewable approval object without committing an action.</summary>
    Task<ApprovalPreview> PreviewAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromException<ApprovalPreview>(NotImplemented(nameof(PreviewAsync)));

    /// <summary>Records an explicit human decision.</summary>
    Task<ApprovalResult> ApproveAsync(ApprovalDecisionRequest request, CancellationToken cancellationToken = default)
        => Task.FromException<ApprovalResult>(NotImplemented(nameof(ApproveAsync)));

    /// <summary>Expires a pending approval.</summary>
    Task<ApprovalResult> ExpireAsync(ApprovalExpirationRequest request, CancellationToken cancellationToken = default)
        => Task.FromException<ApprovalResult>(NotImplemented(nameof(ExpireAsync)));

    /// <summary>Commits an approved preview; M0/M1 adapters should leave this unsupported.</summary>
    Task<ApprovalResult> CommitAsync(ApprovalCommitRequest request, CancellationToken cancellationToken = default)
        => Task.FromException<ApprovalResult>(NotImplemented(nameof(CommitAsync)));

    /// <summary>Verifies expected post-commit state.</summary>
    Task<ApprovalVerification> VerifyAsync(ApprovalVerificationRequest request, CancellationToken cancellationToken = default)
        => Task.FromException<ApprovalVerification>(NotImplemented(nameof(VerifyAsync)));

    private static NotSupportedException NotImplemented(string operation)
        => new($"Approval operation '{operation}' is not implemented by this gateway.");
}

/// <summary>Search and lookup options for the versioned item master.</summary>
public record ItemCatalogQuery
{
    public IReadOnlyCollection<string> ItemIds { get; init; } = Array.Empty<string>();
    public string? ItemId
    {
        get => ItemIds.Count == 1 ? ItemIds.First() : null;
        init => ItemIds = string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };
    }
    public string? Search { get; init; }
    public string? Name { get => Search; init => Search = value; }
    public bool? Tradeable { get; init; }
    public bool? Giftable { get; init; }
    public bool? AccountBound { get; init; }
    public bool IncludeVariants { get; init; } = true;
    public bool IncludeDiagnostics { get; init; }
    public int Limit { get; init; } = 100;
    public string? Cursor { get; init; }
    public string? CatalogVersion { get; init; }

    public ItemCatalogQuery() { }

    public ItemCatalogQuery(string? search, int limit = 100)
    {
        Search = search;
        Limit = limit;
    }

    public void Validate()
    {
        if (Limit <= 0) throw new ArgumentOutOfRangeException(nameof(Limit));
    }
}

/// <summary>Compatibility name for item lookup requests.</summary>
public record ItemCatalogRequest : ItemCatalogQuery
{
    public ItemCatalogRequest() { }
    public ItemCatalogRequest(string? search, int limit = 100) : base(search, limit) { }
}

/// <summary>A packaged/opened or fold/zap relationship in the item master.</summary>
public record ItemVariantLink
{
    public string ItemId { get; init; } = string.Empty;
    public string RelatedItemId { get; init; } = string.Empty;
    public string? Relation { get; init; }
    public string? Tag { get; init; }

    public ItemVariantLink() { }
    public ItemVariantLink(string itemId, string relatedItemId, string? relation = null, string? tag = null)
    {
        ItemId = itemId;
        RelatedItemId = relatedItemId;
        Relation = relation;
        Tag = tag;
    }
}

/// <summary>Stable item facts used by arbitrage and transfer planners.</summary>
public record ItemCatalogEntry
{
    public string ItemId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    public ItemTradeabilityState Tradeability { get; init; } = ItemTradeabilityState.Unknown;
    public bool IsTradeable { get; init; }
    public bool IsGiftable { get; init; }
    public bool IsAccountBound { get; init; }
    public bool CanPurchase { get; init; }
    public bool CanSell { get; init; }
    public bool IsPackage { get; init; }
    public bool CanOpen { get; init; }
    public ItemVariantState VariantKind { get; init; } = ItemVariantState.None;
    public string? PackagedItemId { get; init; }
    public string? OpenedItemId { get; init; }
    public decimal? NpcPrice { get; init; }
    public decimal? AutosellPrice { get; init; }
    public IReadOnlyList<ItemVariantLink> Variants { get; init; } = Array.Empty<ItemVariantLink>();
    public IReadOnlySet<string> Tags { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string? CatalogVersion { get; init; }
    public SnapshotState Snapshot { get; init; } = new();

    public bool Tradeable => IsTradeable;
    public bool Giftable => IsGiftable;
    public bool AccountBound => IsAccountBound;
    public bool IsBound => IsAccountBound;
    public bool IsMallTradeable => IsTradeable && !IsAccountBound;
    public bool IsGiftTransferable => IsGiftable && !IsAccountBound;
    public bool CanGift => IsGiftTransferable;
    public bool CanTrade => IsMallTradeable;
    public bool IsAutosellable => AutosellPrice is > 0;
    public bool CanAutosell => IsAutosellable;
    public bool IsNpcSellable => NpcPrice is > 0;

    public ItemCatalogEntry() { }

    public ItemCatalogEntry(string itemId, string? name = null, ItemTradeabilityState tradeability = ItemTradeabilityState.Unknown)
    {
        ItemId = itemId;
        Name = name;
        Tradeability = tradeability;
        IsTradeable = tradeability == ItemTradeabilityState.Tradeable;
        IsGiftable = tradeability == ItemTradeabilityState.Giftable;
        IsAccountBound = tradeability == ItemTradeabilityState.AccountBound;
    }
}

/// <summary>Result returned from an item catalog query.</summary>
public record ItemCatalogResult
{
    public GatewayState State { get; init; } = GatewayState.Unknown;
    public IReadOnlyList<ItemCatalogEntry> Items { get; init; } = Array.Empty<ItemCatalogEntry>();
    public IReadOnlyList<ItemCatalogEntry> Entries => Items;
    public SnapshotState Snapshot { get; init; } = new();
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();
    public string? NextCursor { get; init; }
    public bool IsStale => Snapshot.IsStale || State == GatewayState.Stale;
    public QueryState Status => State.ToQuery();

    public ItemCatalogResult() { }

    public ItemCatalogResult(QueryState state,
        IEnumerable<ItemCatalogEntry>? items = null,
        SnapshotState? snapshot = null,
        IEnumerable<GatewayDiagnostic>? diagnostics = null,
        string? nextCursor = null)
        : this(state.ToGateway(), items, snapshot, diagnostics, nextCursor) { }

    public ItemCatalogResult(
        GatewayState state,
        IEnumerable<ItemCatalogEntry>? items = null,
        SnapshotState? snapshot = null,
        IEnumerable<GatewayDiagnostic>? diagnostics = null,
        string? nextCursor = null)
    {
        State = state;
        Items = items?.ToArray() ?? Array.Empty<ItemCatalogEntry>();
        Snapshot = snapshot ?? new SnapshotState();
        Diagnostics = diagnostics?.ToArray() ?? Array.Empty<GatewayDiagnostic>();
        NextCursor = nextCursor;
    }
}

/// <summary>Read-side item-master boundary; no parser or transport is exposed.</summary>
public interface IItemCatalogGateway
{
    Task<ItemCatalogResult> QueryAsync(ItemCatalogQuery request, CancellationToken cancellationToken = default)
        => Task.FromException<ItemCatalogResult>(NotImplemented(nameof(QueryAsync)));

    Task<ItemCatalogResult> GetAsync(string itemId, CancellationToken cancellationToken = default)
        => QueryAsync(new ItemCatalogQuery { ItemIds = new[] { itemId }, Limit = 1 }, cancellationToken);

    Task<ItemCatalogResult> SearchAsync(ItemCatalogQuery request, CancellationToken cancellationToken = default)
        => QueryAsync(request, cancellationToken);

    private static NotSupportedException NotImplemented(string operation)
        => new($"Item catalog operation '{operation}' is not implemented by this gateway.");
}

/// <summary>One expected-price-bound own-shop change.</summary>
public record ShopPriceChange
{
    public string ItemId { get; init; } = string.Empty;
    public decimal ExpectedCurrentPrice { get; init; }
    public decimal NewPrice { get; init; }
    public int? ExpectedPurchaseLimit { get; init; }
    public int? NewPurchaseLimit { get; init; }

    public ShopPriceChange() { }

    public ShopPriceChange(
        string itemId,
        decimal expectedCurrentPrice,
        decimal newPrice,
        int? expectedPurchaseLimit = null,
        int? newPurchaseLimit = null)
    {
        ItemId = itemId;
        ExpectedCurrentPrice = expectedCurrentPrice;
        NewPrice = newPrice;
        ExpectedPurchaseLimit = expectedPurchaseLimit;
        NewPurchaseLimit = newPurchaseLimit;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ItemId)) throw new ArgumentException("Item id is required.", nameof(ItemId));
        if (ExpectedCurrentPrice < 0) throw new ArgumentOutOfRangeException(nameof(ExpectedCurrentPrice));
        if (NewPrice < 0) throw new ArgumentOutOfRangeException(nameof(NewPrice));
        if (ExpectedPurchaseLimit is < 0 || NewPurchaseLimit is < 0)
            throw new ArgumentOutOfRangeException(nameof(ExpectedPurchaseLimit));
    }
}

/// <summary>Compatibility name for a price update in a mutation batch.</summary>
public record ShopMutationChange : ShopPriceChange
{
    public ShopMutationChange() { }
    public ShopMutationChange(string itemId, decimal expectedCurrentPrice, decimal newPrice,
        int? expectedPurchaseLimit = null, int? newPurchaseLimit = null)
        : base(itemId, expectedCurrentPrice, newPrice, expectedPurchaseLimit, newPurchaseLimit) { }
}

/// <summary>Preview request for own-shop changes. It carries no transport token.</summary>
public record ShopMutationRequest
{
    public string? AccountId { get; init; }
    public IReadOnlyList<ShopPriceChange> Changes { get; init; } = Array.Empty<ShopPriceChange>();
    public IReadOnlyList<ShopPriceChange> Updates => Changes;
    public IReadOnlyList<ShopPriceChange> PriceUpdates => Changes;
    public string? Reason { get; init; }
    public string? IdempotencyKey { get; init; }
    public bool RequireApproval { get; init; } = true;

    public ShopMutationRequest() { }

    public ShopMutationRequest(IEnumerable<ShopPriceChange> changes, string? accountId = null, string? reason = null)
    {
        Changes = changes?.ToArray() ?? throw new ArgumentNullException(nameof(changes));
        AccountId = accountId;
        Reason = reason;
    }

    public void Validate()
    {
        if (Changes.Count == 0) throw new ArgumentException("At least one shop change is required.", nameof(Changes));
        foreach (var change in Changes) change.Validate();
        if (Changes.GroupBy(x => x.ItemId, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new ArgumentException("Each item may occur only once in a mutation batch.", nameof(Changes));
    }
}

/// <summary>Per-item result in a shop mutation preview or verification.</summary>
public record ShopMutationItemResult
{
    public string ItemId { get; init; } = string.Empty;
    public decimal ExpectedCurrentPrice { get; init; }
    public decimal? ObservedCurrentPrice { get; init; }
    public decimal NewPrice { get; init; }
    public int? ExpectedPurchaseLimit { get; init; }
    public int? ObservedPurchaseLimit { get; init; }
    public ShopMutationState State { get; init; } = ShopMutationState.Unknown;
    public string? Message { get; init; }

    public ShopMutationItemResult() { }

    public ShopMutationItemResult(string itemId, decimal expectedCurrentPrice, decimal newPrice, ShopMutationState state)
    {
        ItemId = itemId;
        ExpectedCurrentPrice = expectedCurrentPrice;
        NewPrice = newPrice;
        State = state;
    }

    public ShopMutationItemResult(string itemId, decimal expectedCurrentPrice, decimal? observedCurrentPrice,
        decimal newPrice, int? expectedPurchaseLimit, int? observedPurchaseLimit,
        ShopMutationState state, string? message = null)
    {
        ItemId = itemId;
        ExpectedCurrentPrice = expectedCurrentPrice;
        ObservedCurrentPrice = observedCurrentPrice;
        NewPrice = newPrice;
        ExpectedPurchaseLimit = expectedPurchaseLimit;
        ObservedPurchaseLimit = observedPurchaseLimit;
        State = state;
        Message = message;
    }
}

/// <summary>Result of validating expected prices against a fresh shop snapshot.</summary>
public record ShopMutationPreview
{
    public string PreviewId { get; init; } = string.Empty;
    public string? OperationId { get; init; }
    public ShopMutationState State { get; init; } = ShopMutationState.Unknown;
    public bool RequiresApproval { get; init; } = true;
    public IReadOnlyList<ShopMutationItemResult> Results { get; init; } = Array.Empty<ShopMutationItemResult>();
    public ExpectedState Expected { get; init; } = new();
    public SnapshotState Snapshot { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();
    public PreviewState Status => State switch
    {
        ShopMutationState.Ready => PreviewState.Ready,
        ShopMutationState.ApprovalRequired => PreviewState.ApprovalRequired,
        ShopMutationState.Conflict => PreviewState.Conflict,
        ShopMutationState.Committed => PreviewState.Committed,
        ShopMutationState.Verified => PreviewState.Verified,
        ShopMutationState.Rejected => PreviewState.Rejected,
        ShopMutationState.Failed => PreviewState.Error,
        _ => PreviewState.Unknown
    };

    public ShopMutationPreview() { }

    public ShopMutationPreview(string previewId, ShopMutationState state,
        IEnumerable<ShopMutationItemResult>? results = null, bool requiresApproval = true)
    {
        PreviewId = previewId;
        State = state;
        Results = results?.ToArray() ?? Array.Empty<ShopMutationItemResult>();
        RequiresApproval = requiresApproval;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Explicit commit request for a previously approved shop preview.</summary>
public record ShopMutationCommitRequest
{
    public string PreviewId { get; init; } = string.Empty;
    public string ApprovalId { get; init; } = string.Empty;
    public string ApprovalToken { get; init; } = string.Empty;
    public string? IdempotencyKey { get; init; }
    public ExpectedState? Expected { get; init; }

    public ShopMutationCommitRequest() { }
    public ShopMutationCommitRequest(string previewId, string approvalId, string approvalToken)
    {
        PreviewId = previewId;
        ApprovalId = approvalId;
        ApprovalToken = approvalToken;
    }
}

/// <summary>Result of a commit or post-commit read.</summary>
public record ShopMutationResult
{
    public string PreviewId { get; init; } = string.Empty;
    public ShopMutationState State { get; init; } = ShopMutationState.Unknown;
    public bool Committed { get; init; }
    public bool Verified { get; init; }
    public IReadOnlyList<ShopMutationItemResult> Results { get; init; } = Array.Empty<ShopMutationItemResult>();
    public SnapshotState Snapshot { get; init; } = new();
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();
    public DateTimeOffset ObservedAt { get; init; }
    public PreviewState Status => State switch
    {
        ShopMutationState.Ready => PreviewState.Ready,
        ShopMutationState.ApprovalRequired => PreviewState.ApprovalRequired,
        ShopMutationState.Conflict => PreviewState.Conflict,
        ShopMutationState.Committed => PreviewState.Committed,
        ShopMutationState.Verified => PreviewState.Verified,
        ShopMutationState.Rejected => PreviewState.Rejected,
        ShopMutationState.Failed => PreviewState.Error,
        _ => PreviewState.Unknown
    };

    public ShopMutationResult() { }

    public ShopMutationResult(string previewId, ShopMutationState state, bool committed = false, bool verified = false)
    {
        PreviewId = previewId;
        State = state;
        Committed = committed;
        Verified = verified;
        ObservedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Own-shop mutation boundary. M0/M1 implementations may be preview-only.</summary>
public interface IShopMutationGateway
{
    Task<ShopMutationPreview> PreviewAsync(ShopMutationRequest request, CancellationToken cancellationToken = default)
        => Task.FromException<ShopMutationPreview>(NotImplemented(nameof(PreviewAsync)));

    Task<ShopMutationResult> CommitAsync(ShopMutationCommitRequest request, CancellationToken cancellationToken = default)
        => Task.FromException<ShopMutationResult>(NotImplemented(nameof(CommitAsync)));

    Task<ShopMutationResult> VerifyAsync(string previewId, CancellationToken cancellationToken = default)
        => Task.FromException<ShopMutationResult>(NotImplemented(nameof(VerifyAsync)));

    Task<ShopMutationPreview> PlanAsync(ShopMutationRequest request, CancellationToken cancellationToken = default)
        => PreviewAsync(request, cancellationToken);

    private static NotSupportedException NotImplemented(string operation)
        => new($"Shop mutation operation '{operation}' is not implemented by this gateway.");
}

/// <summary>Immutable event written for observations, previews and mutations.</summary>
public record AuditEvent
{
    public string EventId { get; init; } = string.Empty;
    public AuditEventKind Kind { get; init; } = AuditEventKind.Observation;
    public string? OperationId { get; init; }
    public string? AccountId { get; init; }
    public string? Source { get; init; }
    public string? RequestId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public SnapshotFreshness Freshness { get; init; } = SnapshotFreshness.Unknown;
    public bool IsStale { get; init; }
    public IReadOnlyDictionary<string, string> Data { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public AuditEvent() { }

    public AuditEvent(AuditEventKind kind, DateTimeOffset occurredAt, string? operationId = null)
    {
        EventId = Guid.NewGuid().ToString("N");
        Kind = kind;
        OccurredAt = occurredAt;
        OperationId = operationId;
    }
}

/// <summary>Bounded audit stream query.</summary>
public record AuditLogQuery
{
    public string? OperationId { get; init; }
    public string? AccountId { get; init; }
    public AuditEventKind? Kind { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public DateTimeOffset? Since { get => From; init => From = value; }
    public DateTimeOffset? Until { get => To; init => To = value; }
    public int Limit { get; init; } = 100;
    public string? Cursor { get; init; }
    public bool IncludeStale { get; init; } = true;

    public AuditLogQuery() { }
    public AuditLogQuery(string? operationId, int limit = 100)
    {
        OperationId = operationId;
        Limit = limit;
    }

    public void Validate()
    {
        if (Limit <= 0) throw new ArgumentOutOfRangeException(nameof(Limit));
        if (From is { } from && To is { } to && to < from)
            throw new ArgumentException("To must be at or after From.", nameof(To));
    }
}

/// <summary>Result of a bounded audit stream read.</summary>
public record AuditLogResult
{
    public GatewayState State { get; init; } = GatewayState.Unknown;
    public IReadOnlyList<AuditEvent> Events { get; init; } = Array.Empty<AuditEvent>();
    public string? NextCursor { get; init; }
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();

    public AuditLogResult() { }
    public AuditLogResult(GatewayState state, IEnumerable<AuditEvent>? events = null, string? nextCursor = null)
    {
        State = state;
        Events = events?.ToArray() ?? Array.Empty<AuditEvent>();
        NextCursor = nextCursor;
    }
}

/// <summary>Write acknowledgment for an audit event.</summary>
public record AuditAppendResult
{
    public GatewayState State { get; init; } = GatewayState.Unknown;
    public string EventId { get; init; } = string.Empty;
    public DateTimeOffset AppendedAt { get; init; }
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();

    public AuditAppendResult() { }
    public AuditAppendResult(GatewayState state, string eventId, DateTimeOffset? appendedAt = null)
    {
        State = state;
        EventId = eventId;
        AppendedAt = appendedAt ?? DateTimeOffset.UtcNow;
    }
}

/// <summary>Append/query boundary for immutable operational and source events.</summary>
public interface IAuditLogGateway
{
    Task<AuditAppendResult> AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        => Task.FromException<AuditAppendResult>(NotImplemented(nameof(AppendAsync)));

    Task<AuditLogResult> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
        => Task.FromException<AuditLogResult>(NotImplemented(nameof(QueryAsync)));

    Task<AuditLogResult> ReadAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
        => QueryAsync(query, cancellationToken);

    Task<AuditAppendResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        => AppendAsync(auditEvent, cancellationToken);

    private static NotSupportedException NotImplemented(string operation)
        => new($"Audit log operation '{operation}' is not implemented by this gateway.");
}

/// <summary>Read and preview arbitrage operations without coupling to transport or UI.</summary>
public interface IArbitrageGateway
{
    Task<ArbitrageQueryResult> QueryAsync(ArbitrageQuery query, CancellationToken cancellationToken = default)
        => Task.FromException<ArbitrageQueryResult>(NotImplemented(nameof(QueryAsync)));

    Task<ArbitragePreview> PreviewAsync(ArbitragePreviewRequest request, CancellationToken cancellationToken = default)
        => Task.FromException<ArbitragePreview>(NotImplemented(nameof(PreviewAsync)));

    Task<ArbitrageQueryResult> GetCandidatesAsync(ArbitrageQuery query, CancellationToken cancellationToken = default)
        => QueryAsync(query, cancellationToken);

    Task<ArbitragePreview> PlanAsync(ArbitragePreviewRequest request, CancellationToken cancellationToken = default)
        => PreviewAsync(request, cancellationToken);

    private static NotSupportedException NotImplemented(string operation)
        => new($"Arbitrage operation '{operation}' is not implemented by this gateway.");
}

#pragma warning restore CS1591
