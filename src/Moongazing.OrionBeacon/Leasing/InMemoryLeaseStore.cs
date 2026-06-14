namespace Moongazing.OrionBeacon.Leasing;

/// <summary>
/// A process-local <see cref="ILeaseStore"/>. Correct for a single node and for tests; it elects a
/// leader only among candidates in the same process. Atomicity is provided by a lock around the
/// acquire/renew/release critical section, and the fencing token increases on every new term.
/// </summary>
public sealed class InMemoryLeaseStore : ILeaseStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = [];
    private readonly Func<DateTimeOffset> now;

    /// <summary>Create a store using the system clock.</summary>
    public InMemoryLeaseStore()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    internal InMemoryLeaseStore(Func<DateTimeOffset> now)
    {
        ArgumentNullException.ThrowIfNull(now);
        this.now = now;
    }

    /// <inheritdoc />
    public Task<LeaseAcquisition> TryAcquireOrRenewAsync(
        string resource, string candidateId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(candidateId);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Lease duration must be positive.");
        }

        lock (gate)
        {
            var timestamp = now();
            var expiresAt = timestamp + duration;

            if (entries.TryGetValue(resource, out var entry) && entry.ExpiresAt > timestamp)
            {
                if (string.Equals(entry.HolderId, candidateId, StringComparison.Ordinal))
                {
                    var renewed = entry with { ExpiresAt = expiresAt };
                    entries[resource] = renewed;
                    return Task.FromResult(LeaseAcquisition.Renewed(
                        new Lease(resource, candidateId, renewed.FencingToken, renewed.AcquiredAt, expiresAt)));
                }

                return Task.FromResult(LeaseAcquisition.Denied(entry.HolderId));
            }

            var token = (entry?.FencingToken ?? 0) + 1;
            entries[resource] = new Entry(candidateId, token, timestamp, expiresAt);
            return Task.FromResult(LeaseAcquisition.Acquired(
                new Lease(resource, candidateId, token, timestamp, expiresAt)));
        }
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string resource, string candidateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(candidateId);

        lock (gate)
        {
            if (entries.TryGetValue(resource, out var entry)
                && string.Equals(entry.HolderId, candidateId, StringComparison.Ordinal))
            {
                // Keep the entry as an expired tombstone so the fencing token keeps climbing across
                // the handover instead of resetting to 1.
                entries[resource] = entry with { ExpiresAt = DateTimeOffset.MinValue };
            }
        }

        return Task.CompletedTask;
    }

    private sealed record Entry(string HolderId, long FencingToken, DateTimeOffset AcquiredAt, DateTimeOffset ExpiresAt);
}
