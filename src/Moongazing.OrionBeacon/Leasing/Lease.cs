namespace Moongazing.OrionBeacon.Leasing;

/// <summary>
/// A held leadership lease over a named resource. The <see cref="FencingToken"/> is a strictly
/// increasing number assigned each time the lease changes hands; a downstream resource that
/// records the highest token it has seen can reject a write from a stale leader whose lease
/// lapsed and was taken over, which closes the classic split-brain gap.
/// </summary>
public sealed class Lease
{
    /// <summary>Create a lease.</summary>
    /// <param name="resource">The contended resource name.</param>
    /// <param name="holderId">The candidate currently holding the lease.</param>
    /// <param name="fencingToken">The monotonically increasing token for this leadership term.</param>
    /// <param name="acquiredAt">When the current term began.</param>
    /// <param name="expiresAt">When the lease lapses unless renewed.</param>
    public Lease(string resource, string holderId, long fencingToken, DateTimeOffset acquiredAt, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(holderId);
        Resource = resource;
        HolderId = holderId;
        FencingToken = fencingToken;
        AcquiredAt = acquiredAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>The contended resource name.</summary>
    public string Resource { get; }

    /// <summary>The candidate holding the lease.</summary>
    public string HolderId { get; }

    /// <summary>The fencing token for this leadership term.</summary>
    public long FencingToken { get; }

    /// <summary>When the current term began.</summary>
    public DateTimeOffset AcquiredAt { get; }

    /// <summary>When the lease lapses unless renewed.</summary>
    public DateTimeOffset ExpiresAt { get; }
}
