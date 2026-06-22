using Moongazing.OrionBeacon.Leasing;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// Runs the shared <see cref="LeaseStoreConformanceTests"/> against the in-process
/// <see cref="InMemoryLeaseStore"/>. This run needs no Docker and is the suite's own validation: if
/// the reference store that ships with the core did not satisfy the contract the suite encodes, the
/// suite would be wrong. It uses the system clock, so the short-lease expiry case lapses for real.
/// </summary>
public sealed class InMemoryLeaseStoreConformanceTests : LeaseStoreConformanceTests
{
    /// <inheritdoc />
    protected override ILeaseStore CreateStore() => new InMemoryLeaseStore();
}
