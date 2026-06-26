using System.Diagnostics;

using Microsoft.Data.SqlClient;

using Npgsql;

using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// One real PostgreSQL container (Testcontainers) shared by every test in the Postgres conformance
/// class. A single shared container is faster and far less exposed to transient Docker-daemon blips
/// than a fresh container per test; the tests stay isolated because each uses a unique resource name.
/// </summary>
/// <remarks>
/// Requires a working Docker daemon, so this run is exercised in CI (and locally where Docker is
/// present). The in-memory run of the same suite needs no Docker and validates the suite everywhere.
/// </remarks>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder().Build();

    /// <summary>The connection string to the running Postgres, valid for the fixture's lifetime.</summary>
    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await RelationalContainerStartup.StartWithWarmupAsync(
            container,
            // Generous budget for a heavily loaded multi-container runner; Postgres warms up fast once
            // the daemon is responsive, so two minutes is ample headroom over the real need.
            budget: TimeSpan.FromMinutes(2),
            warmupAsync: async ct =>
            {
                await using var connection = new NpgsqlConnection(container.GetConnectionString());
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT 1";
                _ = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        ConnectionString = container.GetConnectionString();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

/// <summary>
/// One real SQL Server container (Testcontainers) shared by every test in the SQL Server conformance
/// class. Tests stay isolated by using a unique resource name each, not container isolation.
/// </summary>
/// <remarks>
/// Requires a working Docker daemon, so this run is exercised in CI (and locally where Docker is
/// present). SQL Server is the slowest of the suite's containers to accept logins, so it is given the
/// largest warm-up budget.
/// </remarks>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder().Build();

    /// <summary>The connection string to the running SQL Server, valid for the fixture's lifetime.</summary>
    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await RelationalContainerStartup.StartWithWarmupAsync(
            container,
            // SQL Server is the slowest to accept logins on a loaded runner; give it the most headroom.
            budget: TimeSpan.FromMinutes(5),
            warmupAsync: async ct =>
            {
                await using var connection = new SqlConnection(container.GetConnectionString());
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT 1";
                _ = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        ConnectionString = container.GetConnectionString();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

/// <summary>
/// Shared helper that starts a Testcontainers container and runs a real application-protocol warm-up,
/// retrying both with exponential backoff under an overall time budget.
/// </summary>
/// <remarks>
/// This is the single biggest lever against a flaky container suite: a slow first connection on a
/// loaded runner is retried rather than failing an entire provider test class. The container's own
/// module readiness probe is left in place (it checks the real protocol inside the container); this
/// only adds tolerance for transient faults during start and the first out-of-container connection.
/// </remarks>
internal static class RelationalContainerStartup
{
    /// <summary>
    /// Starts <paramref name="container"/> and runs <paramref name="warmupAsync"/> (a real protocol
    /// round-trip that closes over the strongly-typed container), retrying the whole sequence on
    /// transient failure with exponential backoff until it succeeds or <paramref name="budget"/> is
    /// exhausted, after which the last error is rethrown.
    /// </summary>
    public static async Task StartWithWarmupAsync(
        DotNet.Testcontainers.Containers.IContainer container,
        TimeSpan budget,
        Func<CancellationToken, Task> warmupAsync)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(warmupAsync);

        var sw = Stopwatch.StartNew();
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(10);
        var attempt = 0;
        Exception? last = null;

        while (sw.Elapsed < budget)
        {
            attempt++;

            // Guard the remaining budget before creating the CTS: between the loop condition and here the
            // clock can cross the budget, and CancellationTokenSource throws on a non-positive delay.
            var perAttemptBudget = budget - sw.Elapsed;
            if (perAttemptBudget <= TimeSpan.Zero)
            {
                break;
            }

            using var cts = new CancellationTokenSource(perAttemptBudget);
            try
            {
                // StartAsync is idempotent once started, so retrying after a warm-up failure is safe and
                // cheap (it returns immediately when the container is already running).
                await container.StartAsync(cts.Token).ConfigureAwait(false);
                await warmupAsync(cts.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                last = ex;
                var remaining = budget - sw.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var wait = delay < remaining ? delay : remaining;
                await Task.Delay(wait).ConfigureAwait(false);
                delay = delay + delay < maxDelay ? delay + delay : maxDelay;
            }
        }

        throw new InvalidOperationException(
            $"Container '{container.Name}' did not become ready within {budget.TotalSeconds:N0}s after {attempt} attempt(s).",
            last);
    }
}
