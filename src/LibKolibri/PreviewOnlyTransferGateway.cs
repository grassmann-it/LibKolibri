namespace LibKolibri;

/// <summary>
/// Validates cross-account item movement without contacting KoL. A future
/// mutation adapter can consume this preview and require an explicit approval
/// token before submitting any transfer request.
/// </summary>
public sealed class PreviewOnlyTransferGateway : ITransferGateway, IKolibriModule
{
    private readonly TimeProvider _clock;

    /// <summary>Creates a preview gateway using the system clock by default.</summary>
    public PreviewOnlyTransferGateway(TimeProvider? clock = null)
        => _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public string Id => "core.transfers.preview";

    /// <inheritdoc />
    public IReadOnlySet<KolibriCapability> Capabilities { get; } =
        new HashSet<KolibriCapability> { KolibriCapability.Transfers };

    /// <inheritdoc />
    public Task<TransferPreview> PreviewAsync(
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var checks = new List<string>();
        if (string.IsNullOrWhiteSpace(request.SourceAccount))
            checks.Add("Source account is required.");
        if (string.IsNullOrWhiteSpace(request.DestinationAccount))
            checks.Add("Destination account is required.");
        if (!string.IsNullOrWhiteSpace(request.SourceAccount)
            && string.Equals(request.SourceAccount, request.DestinationAccount, StringComparison.OrdinalIgnoreCase))
            checks.Add("Source and destination accounts must differ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            checks.Add("A transfer reason is required for the audit log.");
        if (request.Items is null || request.Items.Count == 0)
        {
            checks.Add("At least one item quantity is required.");
        }
        else
        {
            foreach (var (itemId, quantity) in request.Items)
            {
                if (string.IsNullOrWhiteSpace(itemId))
                    checks.Add("Item ids must not be empty.");
                if (quantity <= 0)
                    checks.Add($"Item {itemId} must have a positive quantity.");
            }
        }

        var isValid = checks.Count == 0;
        return Task.FromResult(new TransferPreview(
            request,
            RequiresApproval: isValid,
            Checks: checks,
            CreatedAt: _clock.GetUtcNow()));
    }
}
