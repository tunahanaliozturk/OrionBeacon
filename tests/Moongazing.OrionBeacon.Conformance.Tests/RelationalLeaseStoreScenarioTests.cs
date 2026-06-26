using System.Data.Common;
using System.Diagnostics;

using Microsoft.Data.SqlClient;

using Moongazing.OrionBeacon.Leasing;
using Moongazing.OrionBeacon.Stores.Relational;

using Npgsql;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// Database-backed regression tests for the relational store's bootstrap and timing edges that the
/// shared conformance suite does not cover: concurrent first-time schema creation must not fail, and a
/// sub-second lease must expire at the fractional time it was given rather than being rounded up to a
/// whole second.
/// </summary>
/// <remarks>
/// Requires a working Docker daemon. The white-box validation edges (provider required, command timeout
/// positive, table name shape) are proven without a database by <see cref="RelationalDialectTests"/>;
/// these need a real engine because they exercise DDL races and the engine clock.
/// </remarks>
public abstract class RelationalLeaseStoreScenarioTests
{
    /// <summary>A new provider connection to the fixture's database each call.</summary>
    protected abstract DbConnection CreateConnection();

    /// <summary>The engine under test, selecting the dialect.</summary>
    protected abstract RelationalProvider Provider { get; }

    private RelationalLeaseStore CreateStore(string tableName)
        => new(
            CreateConnection,
            new RelationalLeaseStoreOptions { Provider = Provider, TableName = tableName });

    /// <summary>
    /// Many candidates racing the very first use of a brand-new table all bootstrap the schema
    /// concurrently. The check-then-create on SQL Server (and the catalog race on Postgres) must be
    /// serialised so no node fails with "object already exists" / a duplicate catalog key, and exactly
    /// one candidate must still win the lease.
    /// </summary>
    [Fact]
    public async Task Concurrent_first_use_bootstraps_the_schema_without_failing()
    {
        // A table name unique to this test so every candidate is genuinely racing a first-time create.
        var tableName = "ob_boot_" + Guid.NewGuid().ToString("N");
        var resource = "res-" + Guid.NewGuid().ToString("N");
        const int candidates = 24;

        // A fresh store instance per candidate so the per-store "schema already ensured" flag never
        // short-circuits the race: every candidate must run the create-if-absent against the same new
        // table at the same time.
        var attempts = Enumerable.Range(0, candidates)
            .Select(i => Task.Run(async () =>
            {
                var store = CreateStore(tableName);
                return await store.TryAcquireOrRenewAsync(
                    resource, $"cand-{i}", TimeSpan.FromSeconds(30));
            }))
            .ToArray();

        var results = await Task.WhenAll(attempts);

        // No candidate threw: every bootstrap succeeded. And the contract still holds: exactly one win.
        var winners = results.Where(r => r.Outcome == LeaseOutcome.Acquired).ToArray();
        Assert.Single(winners);
        Assert.All(
            results.Where(r => r.Outcome != LeaseOutcome.Acquired),
            r => Assert.Equal(LeaseOutcome.Denied, r.Outcome));
    }

    /// <summary>
    /// A fractional-second lease (1500 ms) must be honoured at its real length: it is still held a
    /// short time after acquisition (proving it was not floored to ~1 s or below), and it does lapse so
    /// a follower can take over (proving it was not rounded up to a longer whole second).
    /// </summary>
    [Fact]
    public async Task A_fractional_second_lease_expires_at_its_fractional_time()
    {
        var tableName = "ob_frac_" + Guid.NewGuid().ToString("N");
        var resource = "res-" + Guid.NewGuid().ToString("N");
        var store = CreateStore(tableName);

        var lease = TimeSpan.FromMilliseconds(1500);
        var acquired = await store.TryAcquireOrRenewAsync(resource, "a", lease);
        Assert.Equal(LeaseOutcome.Acquired, acquired.Outcome);

        // The reported expiry must reflect the fractional lease, not a whole-second rounding. Allow a
        // small tolerance for engine-side timestamp resolution, but require it to be clearly sub-2s and
        // clearly more than 1s so neither a floor-to-1s nor a ceil-to-2s could pass.
        var span = acquired.Lease!.ExpiresAt - acquired.Lease.AcquiredAt;
        Assert.True(
            span > TimeSpan.FromMilliseconds(1400) && span < TimeSpan.FromMilliseconds(1600),
            $"expected a ~1500 ms lease span but the engine recorded {span.TotalMilliseconds:N0} ms");

        // Well within the lease (long before 1500 ms) a follower is still denied: the lease was not
        // truncated to a sub-second length.
        var earlyDenied = await store.TryAcquireOrRenewAsync(resource, "b", TimeSpan.FromSeconds(30));
        Assert.Equal(LeaseOutcome.Denied, earlyDenied.Outcome);

        // And the lease does lapse: a follower eventually takes over. Poll past 1500 ms with generous
        // headroom for a loaded runner, but the takeover proves the lease was not rounded up to a much
        // longer whole-second value.
        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromSeconds(10);
        LeaseAcquisition? takeover = null;
        while (sw.Elapsed < deadline)
        {
            var attempt = await store.TryAcquireOrRenewAsync(resource, "b", TimeSpan.FromSeconds(30));
            if (attempt.Outcome == LeaseOutcome.Acquired)
            {
                takeover = attempt;
                break;
            }

            await Task.Delay(50);
        }

        Assert.NotNull(takeover);
        Assert.Equal("b", takeover!.Lease!.HolderId);
        Assert.True(takeover.Lease.FencingToken > acquired.Lease.FencingToken);
    }
}

/// <summary>The relational scenario tests over a PostgreSQL container.</summary>
public sealed class PostgresRelationalScenarioTests
    : RelationalLeaseStoreScenarioTests, IClassFixture<PostgresContainerFixture>
{
    private readonly string connectionString;

    public PostgresRelationalScenarioTests(PostgresContainerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        connectionString = fixture.ConnectionString;
    }

    protected override RelationalProvider Provider => RelationalProvider.Postgres;

    protected override DbConnection CreateConnection() => new NpgsqlConnection(connectionString);
}

/// <summary>The relational scenario tests over a SQL Server container.</summary>
public sealed class SqlServerRelationalScenarioTests
    : RelationalLeaseStoreScenarioTests, IClassFixture<SqlServerContainerFixture>
{
    private readonly string connectionString;

    public SqlServerRelationalScenarioTests(SqlServerContainerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        connectionString = fixture.ConnectionString;
    }

    protected override RelationalProvider Provider => RelationalProvider.SqlServer;

    protected override DbConnection CreateConnection() => new SqlConnection(connectionString);
}
