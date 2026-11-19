#pragma warning disable CS1591

using CoreTransferApproval = KolArbitrage.Core.Integration.TransferApproval;
using CoreTransferApprovalPolicy = KolArbitrage.Core.Integration.TransferApprovalPolicy;
using CoreTransferApprovalRequest = KolArbitrage.Core.Integration.TransferApprovalRequest;
using CoreTransferCommitRequest = KolArbitrage.Core.Integration.TransferCommitRequest;
using CoreTransferCommitResult = KolArbitrage.Core.Integration.TransferCommitResult;
using CoreTransferIdempotencyStore = KolArbitrage.Core.Integration.ITransferIdempotencyStore;
using CoreTransferObservation = KolArbitrage.Core.Integration.TransferObservation;
using CoreTransferPreview = KolArbitrage.Core.Integration.TransferPreview;
using CoreTransferRequest = KolArbitrage.Core.Integration.TransferRequest;
using CoreTransferReservationStore = KolArbitrage.Core.Integration.ITransferReservationStore;
using CoreTransferVerificationRequest = KolArbitrage.Core.Integration.TransferVerificationRequest;
using CoreTransferVerificationResult = KolArbitrage.Core.Integration.TransferVerificationResult;
using KolArbitrage.Core.Integration;

namespace LibKolibri;

/// <summary>
/// Stable Main/Mallbot transfer workflow boundary. Implementations may bind
/// these contracts to a transport, but the contract itself never contains a
/// KoL route, cookie or credential.
/// </summary>
public interface IMainMallbotTransferGateway
{
    /// <summary>Builds a transfer review without moving an item or Meat.</summary>
    Task<CoreTransferPreview> PreviewAsync(
        CoreTransferRequest request,
        CoreTransferObservation observation,
        CancellationToken cancellationToken = default);

    /// <summary>Records an opaque approval; it has no external side effect.</summary>
    Task<CoreTransferApproval> ApproveAsync(
        CoreTransferPreview preview,
        string? approvedBy = null,
        string? approvalToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an adapter-supplied commit acknowledgement and fresh reads. A
    /// gateway must not issue a live KoL POST as part of this method.
    /// </summary>
    Task<CoreTransferCommitResult> CommitAsync(
        CoreTransferCommitRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Runs verification separately when a host commits elsewhere.</summary>
    Task<CoreTransferVerificationResult> VerifyAsync(
        CoreTransferVerificationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Shorter alias for hosts that expose transfer workflows generically.</summary>
public interface ITransferWorkflowGateway : IMainMallbotTransferGateway
{
}

/// <summary>
/// Pure, preview-only workflow gateway. It supplies all review/verification
/// behavior while intentionally refusing any live mutation, making it suitable
/// for CLI, MCP and offline tests.
/// </summary>
public class PreviewOnlyMainMallbotTransferGateway : ITransferWorkflowGateway, IKolibriModule
{
    private readonly TimeProvider _clock;
    private readonly CoreTransferApprovalPolicy _policy;
    private readonly CoreTransferIdempotencyStore? _idempotency;
    private readonly CoreTransferReservationStore? _reservations;

    public PreviewOnlyMainMallbotTransferGateway(
        CoreTransferApprovalPolicy? policy = null,
        TimeProvider? clock = null,
        CoreTransferIdempotencyStore? idempotency = null,
        CoreTransferReservationStore? reservations = null)
    {
        _policy = policy ?? new CoreTransferApprovalPolicy();
        _policy.Validate();
        _clock = clock ?? TimeProvider.System;
        _idempotency = idempotency;
        _reservations = reservations;
    }

    public string Id => "core.transfers.main-mallbot.preview";

    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Transfers };

    public Task<CoreTransferPreview> PreviewAsync(
        CoreTransferRequest request,
        CoreTransferObservation observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observation);
        return Task.FromResult(TransferApprovalService.Preview(
            request, observation, _policy, _clock.GetUtcNow(), reservations: _reservations));
    }

    public Task<CoreTransferApproval> ApproveAsync(
        CoreTransferPreview preview,
        string? approvedBy = null,
        string? approvalToken = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(preview);
        return Task.FromResult(TransferApprovalService.Approve(
            preview, approvedBy, approvalToken, _clock.GetUtcNow()));
    }

    public Task<CoreTransferCommitResult> CommitAsync(
        CoreTransferCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        // No transport is available here. Verify supplied snapshots for audit
        // purposes, then make the refusal explicit even if a caller supplied a
        // non-dry-run preview. This keeps the class incapable of a live POST.
        var result = TransferApprovalService.Commit(request, _idempotency, _reservations);
        if (result.AbortReasons.Contains(TransferAbortReason.DryRun))
            return Task.FromResult(result);
        var reasons = result.AbortReasons.ToList();
        if (!reasons.Contains(TransferAbortReason.DryRun))
            reasons.Add(TransferAbortReason.DryRun);
        return Task.FromResult(result with
        {
            Status = TransferVerificationStatus.Aborted,
            OperationStatus = TransferOperationStatus.Aborted,
            Committed = false,
            AbortReasons = reasons,
            ReconciliationRequired = false
        });
    }

    public Task<CoreTransferVerificationResult> VerifyAsync(
        CoreTransferVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(TransferApprovalService.Verify(request, _reservations));
    }
}

/// <summary>Compatibility name for clients that call this a transfer workflow gateway.</summary>
public sealed class PreviewOnlyTransferWorkflowGateway : PreviewOnlyMainMallbotTransferGateway
{
    public PreviewOnlyTransferWorkflowGateway(
        CoreTransferApprovalPolicy? policy = null,
        TimeProvider? clock = null,
        CoreTransferIdempotencyStore? idempotency = null,
        CoreTransferReservationStore? reservations = null)
        : base(policy, clock, idempotency, reservations)
    {
    }
}

