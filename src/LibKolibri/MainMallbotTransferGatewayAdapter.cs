#pragma warning disable CS1591

using CoreTransferApproval = KolArbitrage.Core.Integration.TransferApproval;
using CoreTransferApprovalPolicy = KolArbitrage.Core.Integration.TransferApprovalPolicy;
using CoreTransferCommitRequest = KolArbitrage.Core.Integration.TransferCommitRequest;
using CoreTransferCommitResult = KolArbitrage.Core.Integration.TransferCommitResult;
using CoreTransferObservation = KolArbitrage.Core.Integration.TransferObservation;
using CoreTransferPreview = KolArbitrage.Core.Integration.TransferPreview;
using CoreTransferRequest = KolArbitrage.Core.Integration.TransferRequest;
using CoreTransferVerificationRequest = KolArbitrage.Core.Integration.TransferVerificationRequest;
using CoreTransferVerificationResult = KolArbitrage.Core.Integration.TransferVerificationResult;
using KolArbitrage.Core.Accounts;
using KolArbitrage.Core.Integration;

namespace LibKolibri;

/// <summary>
/// LibKolibri bridge for the role-bound Core Main/Mallbot adapter.  The
/// contract methods that receive an observation remain verification-only; the
/// transport-backed overloads are explicit and never hidden behind preview.
/// </summary>
public class CoreMainMallbotTransferGateway : ITransferWorkflowGateway, IKolibriModule
{
    private readonly MainMallbotTransferGateway _inner;

    public CoreMainMallbotTransferGateway(MainMallbotTransferGateway inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public CoreMainMallbotTransferGateway(
        IMainMallbotTransferTransport transport,
        MainMallbotTransferAccountBinding main,
        MainMallbotTransferAccountBinding mallbot,
        ItemMaster? catalog = null,
        MainMallbotTransferPolicy? transferPolicy = null,
        CoreTransferApprovalPolicy? approvalPolicy = null,
        ITransferIdempotencyStore? idempotency = null,
        ITransferReservationStore? reservations = null,
        TimeProvider? clock = null)
        : this(new MainMallbotTransferGateway(transport, main, mallbot, catalog,
            transferPolicy, approvalPolicy, idempotency, reservations, clock))
    {
    }

    public string Id => "core.transfers.main-mallbot";

    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Transfers };

    public Task<CoreTransferPreview> PreviewAsync(
        CoreTransferRequest request,
        CoreTransferObservation observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_inner.Preview(request, observation));
    }

    public Task<CoreTransferPreview> PreviewFromTransportAsync(
        CoreTransferRequest request,
        CancellationToken cancellationToken = default)
        => _inner.PreviewAsync(request, cancellationToken);

    public Task<CoreTransferApproval> ApproveAsync(
        CoreTransferPreview preview,
        string? approvedBy = null,
        string? approvalToken = null,
        CancellationToken cancellationToken = default)
        => _inner.ApproveAsync(preview, approvedBy, approvalToken, cancellationToken);

    public Task<CoreTransferCommitResult> CommitAsync(
        CoreTransferCommitRequest request,
        CancellationToken cancellationToken = default)
        => _inner.CommitAsync(request, cancellationToken);

    public Task<CoreTransferCommitResult> CommitApprovedAsync(
        CoreTransferPreview preview,
        CoreTransferApproval approval,
        CancellationToken cancellationToken = default)
        => _inner.CommitAsync(preview, approval, cancellationToken);

    public Task<CoreTransferVerificationResult> VerifyAsync(
        CoreTransferVerificationRequest request,
        CancellationToken cancellationToken = default)
        => _inner.VerifyAsync(request, cancellationToken);
}

/// <summary>Short compatibility alias for hosts that name adapters directly.</summary>
public sealed class KoLMainMallbotTransferGateway : CoreMainMallbotTransferGateway
{
    public KoLMainMallbotTransferGateway(MainMallbotTransferGateway inner)
        : base(inner)
    {
    }

    public KoLMainMallbotTransferGateway(
        IMainMallbotTransferTransport transport,
        MainMallbotTransferAccountBinding main,
        MainMallbotTransferAccountBinding mallbot,
        ItemMaster? catalog = null,
        MainMallbotTransferPolicy? transferPolicy = null,
        CoreTransferApprovalPolicy? approvalPolicy = null,
        ITransferIdempotencyStore? idempotency = null,
        ITransferReservationStore? reservations = null,
        TimeProvider? clock = null)
        : base(transport, main, mallbot, catalog, transferPolicy, approvalPolicy,
            idempotency, reservations, clock)
    {
    }
}
