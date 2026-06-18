namespace Moongazing.OrionBeacon.Tests;

using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

using Xunit;

/// <summary>
/// Behavior of the hosted election loop: it drives the elector on the renew interval, survives a
/// transient store fault to retry, and resigns on shutdown. A controllable fake elector and short
/// intervals keep these deterministic without real-time delays.
/// </summary>
public sealed class LeaderElectionServiceTests
{
    private static LeaderElectionOptions FastOptions() => new()
    {
        ResourceName = "res",
        CandidateId = "a",
        LeaseDuration = TimeSpan.FromMilliseconds(200),
        RenewInterval = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void Constructor_rejects_a_null_elector()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LeaderElectionService(null!, FastOptions()));
    }

    [Fact]
    public void Constructor_rejects_null_options()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LeaderElectionService(new FakeElector(), null!));
    }

    [Fact]
    public void Constructor_validates_options()
    {
        var bad = FastOptions();
        bad.RenewInterval = bad.LeaseDuration;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LeaderElectionService(new FakeElector(), bad));
    }

    [Fact]
    public async Task The_loop_drives_the_elector_at_least_once_then_resigns_on_stop()
    {
        var elector = new FakeElector();
        var service = new LeaderElectionService(elector, FastOptions());

        await service.StartAsync(CancellationToken.None);
        await elector.WaitForFirstElectAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.True(elector.ElectCount >= 1);
        Assert.Equal(1, elector.ResignCount);
    }

    [Fact]
    public async Task A_transient_store_fault_does_not_kill_the_loop_which_keeps_retrying()
    {
        // First cycle throws; the loop must swallow it and keep electing on later cycles.
        var elector = new FakeElector { ThrowOnFirstElect = true };
        var service = new LeaderElectionService(elector, FastOptions());

        await service.StartAsync(CancellationToken.None);
        await elector.WaitForElectCountAsync(3);
        await service.StopAsync(CancellationToken.None);

        Assert.True(elector.ElectCount >= 3, $"expected continued retries, saw {elector.ElectCount}");
    }

    [Fact]
    public async Task Stopping_resigns_even_when_no_leadership_was_held()
    {
        var elector = new FakeElector { LeaderResult = false };
        var service = new LeaderElectionService(elector, FastOptions());

        await service.StartAsync(CancellationToken.None);
        await elector.WaitForFirstElectAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, elector.ResignCount);
    }

    [Fact]
    public async Task A_resign_fault_during_shutdown_does_not_propagate_when_cancelled()
    {
        // StopAsync only swallows OperationCanceledException from resign; a cancelled shutdown token
        // makes the cancellation-aware resign throw that, which the service must absorb.
        var elector = new FakeElector { CancelAwareResign = true };
        var service = new LeaderElectionService(elector, FastOptions());

        await service.StartAsync(CancellationToken.None);
        await elector.WaitForFirstElectAsync();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Must not throw despite the cancelled resign.
        await service.StopAsync(cts.Token);
    }

    [Fact]
    public async Task Starting_with_an_already_cancelled_token_runs_no_election_cycles()
    {
        var elector = new FakeElector();
        var service = new LeaderElectionService(elector, FastOptions());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, elector.ElectCount);
    }

    /// <summary>A test double for <see cref="ILeaderElector"/> that records calls and can fault on demand.</summary>
    private sealed class FakeElector : ILeaderElector
    {
        private readonly object gate = new();
        private readonly List<TaskCompletionSource> waiters = [];
        private int electCount;

        public bool LeaderResult { get; set; } = true;

        public bool ThrowOnFirstElect { get; set; }

        public bool CancelAwareResign { get; set; }

        public int ElectCount
        {
            get { lock (gate) { return electCount; } }
        }

        public int ResignCount { get; private set; }

        public bool IsLeader { get; private set; }

        public Lease? Lease { get; private set; }

        public Task<bool> TryElectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int count;
            List<TaskCompletionSource> toSignal;
            lock (gate)
            {
                electCount++;
                count = electCount;
                toSignal = [.. waiters];
                waiters.Clear();
            }

            foreach (var w in toSignal)
            {
                w.TrySetResult();
            }

            if (ThrowOnFirstElect && count == 1)
            {
                throw new InvalidOperationException("transient store fault");
            }

            IsLeader = LeaderResult;
            return Task.FromResult(LeaderResult);
        }

        public Task ResignAsync(CancellationToken cancellationToken = default)
        {
            if (CancelAwareResign)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            ResignCount++;
            IsLeader = false;
            return Task.CompletedTask;
        }

        public Task WaitForFirstElectAsync() => WaitForElectCountAsync(1);

        public Task WaitForElectCountAsync(int target)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                if (electCount >= target)
                {
                    return Task.CompletedTask;
                }
                // Re-arm until the target count is reached.
                waiters.Add(tcs);
            }

            return ContinueUntilAsync(tcs.Task, target);
        }

        private async Task ContinueUntilAsync(Task firstSignal, int target)
        {
            await firstSignal.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            while (true)
            {
                lock (gate)
                {
                    if (electCount >= target)
                    {
                        return;
                    }
                }

                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (gate)
                {
                    if (electCount >= target)
                    {
                        return;
                    }
                    waiters.Add(tcs);
                }

                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }
    }
}
