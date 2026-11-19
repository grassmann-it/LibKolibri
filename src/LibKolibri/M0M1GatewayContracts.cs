#pragma warning disable CS1591

using KolArbitrage.Core.Integration;
using KolArbitrage.Core.Pricing;

namespace LibKolibri;

/// <summary>Read-only request for a market price summary/recommendation.</summary>
public record PricingQuery
{
    public string ItemId { get; init; } = string.Empty;
    public decimal? AcquisitionCost { get; init; }
    public bool ForceRefresh { get; init; }

    public PricingQuery() { }

    public PricingQuery(string itemId, decimal? acquisitionCost = null, bool forceRefresh = false)
    {
        ItemId = itemId;
        AcquisitionCost = acquisitionCost;
        ForceRefresh = forceRefresh;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ItemId))
            throw new ArgumentException("Item id is required.", nameof(ItemId));
        if (AcquisitionCost is < 0)
            throw new ArgumentOutOfRangeException(nameof(AcquisitionCost));
    }
}

/// <summary>Compatibility name for pricing requests.</summary>
public record PricingRequest : PricingQuery
{
    public PricingRequest() { }
    public PricingRequest(string itemId, decimal? acquisitionCost = null, bool forceRefresh = false)
        : base(itemId, acquisitionCost, forceRefresh) { }
}

/// <summary>Structured result returned by the pricing gateway.</summary>
public record PricingResult
{
    public GatewayState State { get; init; } = GatewayState.Unknown;
    public string ItemId { get; init; } = string.Empty;
    public PriceSummary? Summary { get; init; }
    public PricingRecommendation? Recommendation { get; init; }
    public SnapshotState Snapshot { get; init; } = new();
    public IReadOnlyList<GatewayDiagnostic> Diagnostics { get; init; } = Array.Empty<GatewayDiagnostic>();
    public bool IsStale => Snapshot.IsStale || State == GatewayState.Stale;
    public bool Succeeded => State is GatewayState.Ready or GatewayState.Empty or GatewayState.Stale;
    public QueryState Status => State.ToQuery();

    public PricingResult() { }

    public PricingResult(
        GatewayState state,
        string itemId,
        PriceSummary? summary = null,
        PricingRecommendation? recommendation = null,
        SnapshotState? snapshot = null,
        IEnumerable<GatewayDiagnostic>? diagnostics = null)
    {
        State = state;
        ItemId = itemId;
        Summary = summary;
        Recommendation = recommendation;
        Snapshot = snapshot ?? new SnapshotState();
        Diagnostics = diagnostics?.ToArray() ?? Array.Empty<GatewayDiagnostic>();
    }
}

/// <summary>Pricing source aggregation and recommendation boundary.</summary>
public interface IPricingGateway
{
    Task<PricingResult> QueryAsync(PricingQuery query, CancellationToken cancellationToken = default)
        => Task.FromException<PricingResult>(NotImplemented(nameof(QueryAsync)));

    Task<PricingResult> GetAsync(string itemId, CancellationToken cancellationToken = default)
        => QueryAsync(new PricingQuery(itemId), cancellationToken);

    Task<PricingResult> RecommendAsync(PricingQuery query, CancellationToken cancellationToken = default)
        => QueryAsync(query, cancellationToken);

    private static NotSupportedException NotImplemented(string operation)
        => new($"Pricing operation '{operation}' is not implemented by this gateway.");
}

/// <summary>Read request for a durable snapshot entry.</summary>
public record SnapshotQuery
{
    public SnapshotKey Key { get; init; }
    public SnapshotCachePolicy? Policy { get; init; }
    public DateTimeOffset? AsOf { get; init; }

    public SnapshotQuery(SnapshotKey key)
        => Key = key ?? throw new ArgumentNullException(nameof(key));
}

/// <summary>Compatibility alias for snapshot reads.</summary>
public record SnapshotRequest : SnapshotQuery
{
    public SnapshotRequest(SnapshotKey key) : base(key) { }
}

/// <summary>Read/write boundary for local, credential-free snapshots.</summary>
public interface ISnapshotGateway
{
    Task<SnapshotEnvelope<T>?> ReadAsync<T>(SnapshotQuery query, CancellationToken cancellationToken = default);

    Task<SnapshotEnvelope<T>> WriteAsync<T>(
        SnapshotKey key,
        T payload,
        DateTimeOffset? fetchedAt = null,
        bool stale = false,
        CancellationToken cancellationToken = default);

    Task<int> InvalidateAsync(SnapshotKey key, CancellationToken cancellationToken = default);

    Task<SnapshotEnvelope<T>?> GetAsync<T>(SnapshotKey key, CancellationToken cancellationToken = default)
        => ReadAsync<T>(new SnapshotQuery(key), cancellationToken);
}

#pragma warning restore CS1591
