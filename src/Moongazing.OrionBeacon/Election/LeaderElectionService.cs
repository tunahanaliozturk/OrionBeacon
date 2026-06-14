namespace Moongazing.OrionBeacon.Election;

using Microsoft.Extensions.Hosting;

/// <summary>
/// The hosted loop that keeps leadership current: it runs the elector on the configured renew
/// interval for the lifetime of the application and resigns on shutdown. A transient store error
/// on one cycle is swallowed so the loop survives to retry; the missed renewal simply lets the
/// lease lapse if the outage outlasts the lease duration, at which point a healthy instance wins.
/// </summary>
public sealed class LeaderElectionService : BackgroundService
{
    private readonly ILeaderElector elector;
    private readonly LeaderElectionOptions options;

    /// <summary>Create the service.</summary>
    /// <param name="elector">The elector to drive.</param>
    /// <param name="options">The election options supplying the renew interval.</param>
    public LeaderElectionService(ILeaderElector elector, LeaderElectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(elector);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.elector = elector;
        this.options = options;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await elector.TryElectAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // keep the loop alive across a transient store fault; retry next cycle
            catch (Exception)
#pragma warning restore CA1031
            {
            }

            try
            {
                await Task.Delay(options.RenewInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await elector.ResignAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown is cancelling; the lease will lapse on its own.
        }
    }
}
