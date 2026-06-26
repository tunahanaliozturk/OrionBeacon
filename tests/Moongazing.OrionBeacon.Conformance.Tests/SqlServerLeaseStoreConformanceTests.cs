using Microsoft.Data.SqlClient;

using Moongazing.OrionBeacon.Leasing;
using Moongazing.OrionBeacon.Stores.Relational;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// Runs the shared <see cref="LeaseStoreConformanceTests"/> against the real
/// <see cref="RelationalLeaseStore"/> over a SQL Server container (Testcontainers). The SQL Server
/// dialect (a single <c>MERGE ... WITH (HOLDLOCK) ... OUTPUT</c>) must satisfy the same contract the
/// Postgres dialect does: atomic acquire-or-renew, a strictly increasing fencing token across
/// leadership changes, lease expiry, and fencing-checked release.
/// </summary>
/// <remarks>
/// Requires Docker. Every test class instance uses a unique table name so a reused container cannot
/// leak lease or fencing-token state between runs, and every test already uses a unique resource name.
/// </remarks>
public sealed class SqlServerLeaseStoreConformanceTests
    : LeaseStoreConformanceTests, IClassFixture<SqlServerContainerFixture>
{
    private readonly string connectionString;
    private readonly string tableName = "ob_leases_" + Guid.NewGuid().ToString("N");

    public SqlServerLeaseStoreConformanceTests(SqlServerContainerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        connectionString = fixture.ConnectionString;
    }

    /// <inheritdoc />
    protected override ILeaseStore CreateStore()
        => new RelationalLeaseStore(
            () => new SqlConnection(connectionString),
            new RelationalLeaseStoreOptions
            {
                Provider = RelationalProvider.SqlServer,
                TableName = tableName,
            });
}
