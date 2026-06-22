using System.Diagnostics;

using StackExchange.Redis;

using Testcontainers.Redis;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// One real Redis container (Testcontainers) shared by every test in the Redis conformance class. A
/// single shared container is faster and far less exposed to transient Docker-daemon blips than a
/// fresh container per test; the tests stay isolated because each uses a unique resource name, not
/// container isolation.
/// </summary>
/// <remarks>
/// Requires a working Docker daemon, so this run is exercised in CI (and locally where Docker is
/// present). The in-memory run of the same suite needs no Docker and validates the suite everywhere.
/// </remarks>
public sealed class RedisContainerFixture : IAsyncLifetime
{
    private readonly RedisContainer container = new RedisBuilder().Build();

    /// <summary>The connection multiplexer to the running Redis, valid for the fixture's lifetime.</summary>
    public IConnectionMultiplexer Mux { get; private set; } = default!;

    public async Task InitializeAsync()
        => Mux = await RedisContainerStartup.StartAndConnectAsync(container).ConfigureAwait(false);

    public async Task DisposeAsync()
    {
        if (Mux is not null)
        {
            await Mux.DisposeAsync().ConfigureAwait(false);
        }

        await container.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Starts a Redis container and opens a verified <see cref="IConnectionMultiplexer"/>, retrying both
/// the container start and the connect-plus-PING warm-up with exponential backoff under an overall
/// budget.
/// </summary>
/// <remarks>
/// On a loaded CI runner the first connection after a container starts can blip (a slow first
/// handshake) and the Docker daemon can transiently fault the start, either of which would otherwise
/// fail the whole class. Retrying the whole sequence rides out those transients. The container's own
/// in-container readiness probe is left in place; this only adds tolerance for the first
/// out-of-container connection.
/// </remarks>
internal static class RedisContainerStartup
{
    /// <summary>
    /// Starts <paramref name="container"/>, connects a multiplexer, and confirms liveness with a real
    /// <c>PING</c>, retrying the whole sequence on transient failure until it succeeds or the budget
    /// is exhausted, after which the last error is rethrown. A partially-built multiplexer from a
    /// failed attempt is disposed before the next try so connections are not leaked.
    /// </summary>
    public static async Task<IConnectionMultiplexer> StartAndConnectAsync(RedisContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        // Redis itself comes up quickly; the budget exists to ride out daemon/handshake blips under load.
        var budget = TimeSpan.FromMinutes(2);
        var sw = Stopwatch.StartNew();
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(10);
        var attempt = 0;
        Exception? last = null;

        while (sw.Elapsed < budget)
        {
            attempt++;
            using var cts = new CancellationTokenSource(budget - sw.Elapsed);
            ConnectionMultiplexer? mux = null;
            try
            {
                await container.StartAsync(cts.Token).ConfigureAwait(false);
                mux = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString()).ConfigureAwait(false);
                await mux.GetDatabase().PingAsync().ConfigureAwait(false);
                return mux;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                last = ex;
                if (mux is not null)
                {
                    await mux.DisposeAsync().ConfigureAwait(false);
                }

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
            $"Redis container '{container.Name}' did not become connectable within {budget.TotalSeconds:N0}s after {attempt} attempt(s).",
            last);
    }
}
