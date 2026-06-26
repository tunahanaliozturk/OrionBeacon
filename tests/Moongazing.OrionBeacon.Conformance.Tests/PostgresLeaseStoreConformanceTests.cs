using Moongazing.OrionBeacon.Leasing;
using Moongazing.OrionBeacon.Stores.Relational;

using Npgsql;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// Runs the shared <see cref="LeaseStoreConformanceTests"/> against the real
/// <see cref="RelationalLeaseStore"/> over a PostgreSQL container (Testcontainers). Passing the same
/// suite the in-memory store passes is what proves the distributed store honours the contract: atomic
/// acquire-or-renew, a strictly increasing fencing token across leadership changes, lease expiry, and
/// fencing-checked release.
/// </summary>
/// <remarks>
/// Requires Docker. Every test class instance uses a unique table name so a reused container cannot
/// leak lease or fencing-token state between runs, and every test already uses a unique resource name.
/// </remarks>
public sealed class PostgresLeaseStoreConformanceTests
    : LeaseStoreConformanceTests, IClassFixture<PostgresContainerFixture>
{
    private readonly string connectionString;
    private readonly string tableName = "ob_leases_" + Guid.NewGuid().ToString("N");

    public PostgresLeaseStoreConformanceTests(PostgresContainerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        connectionString = fixture.ConnectionString;
    }

    /// <inheritdoc />
    protected override ILeaseStore CreateStore()
        => new RelationalLeaseStore(
            () => new NpgsqlConnection(connectionString),
            new RelationalLeaseStoreOptions
            {
                Provider = RelationalProvider.Postgres,
                TableName = tableName,
            });
}
