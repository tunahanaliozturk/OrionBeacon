using Moongazing.OrionBeacon.Stores.Relational;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// White-box tests for the relational store that need no database: that a bad table name is rejected
/// before it can reach a statement, and that a good one is engine-quoted. The behavioural contract
/// (atomic acquire-or-renew, fencing monotonicity, expiry, fencing-checked release) is proven against
/// real engines by <see cref="PostgresLeaseStoreConformanceTests"/> and
/// <see cref="SqlServerLeaseStoreConformanceTests"/>; these guard the one piece of SQL that is
/// interpolated rather than parameterised (the table identifier) so it cannot become an injection
/// vector.
/// </summary>
public sealed class RelationalDialectTests
{
    private static Func<System.Data.Common.DbConnection> NoConnection
        => () => throw new InvalidOperationException("The constructor must not open a connection.");

    [Theory]
    [InlineData(RelationalProvider.Postgres)]
    [InlineData(RelationalProvider.SqlServer)]
    public void A_plain_table_name_is_accepted(RelationalProvider provider)
    {
        // The constructor validates and quotes the table name eagerly, so a successful construction
        // proves the name was accepted without needing a database.
        var store = new RelationalLeaseStore(
            NoConnection,
            new RelationalLeaseStoreOptions { Provider = provider, TableName = "orionbeacon_leases" });

        Assert.NotNull(store);
    }

    [Theory]
    [InlineData("leases; DROP TABLE users")]
    [InlineData("leases\"")]
    [InlineData("leases]")]
    [InlineData("leases-1")]
    [InlineData("1leases")]
    [InlineData("schema.leases")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_table_name_that_is_not_a_plain_identifier_is_rejected(string tableName)
    {
        Assert.Throws<ArgumentException>(() => new RelationalLeaseStore(
            NoConnection,
            new RelationalLeaseStoreOptions
            {
                Provider = RelationalProvider.Postgres,
                TableName = tableName,
            }));
    }

    [Fact]
    public void An_unknown_provider_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelationalLeaseStore(
            NoConnection,
            new RelationalLeaseStoreOptions
            {
                Provider = (RelationalProvider)999,
                TableName = "orionbeacon_leases",
            }));
    }
}
