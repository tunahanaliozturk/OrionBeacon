using System.Data;
using System.Data.Common;

using Moongazing.OrionBeacon.Leasing;

namespace Moongazing.OrionBeacon.Stores.Relational;

/// <summary>
/// A relational <see cref="ILeaseStore"/> that elects a leader across a cluster over PostgreSQL or
/// SQL Server. A single leader row per resource holds the current holder, the fencing token, and the
/// lease expiry. Acquire-or-renew is one atomic conditional upsert (PostgreSQL
/// <c>INSERT ... ON CONFLICT ... RETURNING</c>, SQL Server <c>MERGE ... OUTPUT</c>), never a
/// read-then-write, so under concurrent acquires exactly one candidate wins. The fencing token lives
/// in the row, advances by one on every new leadership term, and is left unchanged on a renew, so it
/// is strictly increasing across leadership changes and stable across a renew. The engine clock
/// decides liveness, so candidates with skewed wall clocks still agree on whether a lease is live.
/// </summary>
/// <remarks>
/// <para>
/// The store is given a connection factory rather than a connection so it can open a short-lived
/// connection per operation and let the provider's pool recycle it, which suits the elector's renew
/// loop and avoids holding a session open between cycles. The factory returns a provider-specific
/// <see cref="DbConnection"/> (an <c>NpgsqlConnection</c> or a <c>SqlConnection</c>); the store talks
/// to it only through <see cref="System.Data.Common"/>, so it depends on no provider type itself and
/// the same code path serves both engines, differing only in the SQL the <see cref="RelationalDialect"/>
/// supplies.
/// </para>
/// <para>
/// The table is created on first use if <see cref="RelationalLeaseStoreOptions"/> is left at its
/// default; the create is idempotent and races between nodes are resolved by the engine (create-if-
/// absent). The initial insert race between two nodes creating the very first leader row is resolved
/// by the primary key on <c>resource</c> inside the atomic upsert, so the loser converts to the
/// conditional-update branch instead of failing with a duplicate key.
/// </para>
/// </remarks>
public sealed class RelationalLeaseStore : ILeaseStore
{
    private readonly Func<DbConnection> connectionFactory;
    private readonly RelationalLeaseStoreOptions options;
    private readonly RelationalDialect dialect;
    private readonly string quotedTable;
    private readonly int commandTimeoutSeconds;
    private volatile bool schemaEnsured;

    /// <summary>Create the store over a connection factory.</summary>
    /// <param name="connectionFactory">
    /// Returns a new, unopened provider connection each call (for example
    /// <c>() =&gt; new NpgsqlConnection(connectionString)</c>). The store opens and disposes the
    /// connection itself, once per operation.
    /// </param>
    /// <param name="options">
    /// The store options. <see cref="RelationalLeaseStoreOptions.Provider"/> selects the SQL dialect and
    /// must match the connection the factory returns.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or
    /// <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The configured table name is not a plain identifier.</exception>
    public RelationalLeaseStore(Func<DbConnection> connectionFactory, RelationalLeaseStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(options);

        this.connectionFactory = connectionFactory;
        this.options = options;
        dialect = RelationalDialect.For(options.Provider);
        quotedTable = dialect.QuoteTable(options.TableName);
        commandTimeoutSeconds = checked((int)Math.Ceiling(options.CommandTimeout.TotalSeconds));
        if (commandTimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.CommandTimeout, "Command timeout must not be negative.");
        }
    }

    /// <inheritdoc />
    public async Task<LeaseAcquisition> TryAcquireOrRenewAsync(
        string resource, string candidateId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Lease duration must be positive.");
        }

        var ttlSeconds = duration.TotalSeconds;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = CreateCommand(connection, dialect.AcquireOrRenewSql(quotedTable)))
        {
            AddParameter(command, RelationalParameters.Resource, resource);
            AddParameter(command, RelationalParameters.Holder, candidateId);
            AddParameter(command, RelationalParameters.TtlSeconds, ttlSeconds);

            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var token = reader.GetInt64(0);
                var acquiredAt = ReadUtc(reader, 1);
                var expiresAt = ReadUtc(reader, 2);
                var renewed = reader.GetBoolean(3);
                var lease = new Lease(resource, candidateId, token, acquiredAt, expiresAt);
                return renewed ? LeaseAcquisition.Renewed(lease) : LeaseAcquisition.Acquired(lease);
            }
        }

        // No row from the conditional upsert means a different candidate holds a live lease. Look up
        // who, on the same connection. If the holder released or its lease lapsed in the gap between the
        // two reads, report the candidate itself as the holder is unknown; the elector will retry.
        var holder = await ReadCurrentHolderAsync(connection, resource, cancellationToken).ConfigureAwait(false);
        return LeaseAcquisition.Denied(holder ?? candidateId);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        string resource, string candidateId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, dialect.ReleaseSql(quotedTable));
        AddParameter(command, RelationalParameters.Resource, resource);
        AddParameter(command, RelationalParameters.Holder, candidateId);

        // Fencing-checked by the WHERE holder = @holder; a non-holder caller updates zero rows.
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ReadCurrentHolderAsync(
        DbConnection connection, string resource, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, dialect.CurrentHolderSql(quotedTable));
        AddParameter(command, RelationalParameters.Resource, resource);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is string holder ? holder : null;
    }

    private async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory()
            ?? throw new InvalidOperationException("The connection factory returned null.");
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task EnsureSchemaAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (schemaEnsured)
        {
            return;
        }

        // The create is idempotent (create-if-absent on both engines), so no lock is needed: if two
        // first-time callers race, both run a harmless no-op create and both set the flag. Keeping the
        // store lock-free avoids owning a disposable synchronisation primitive on a type whose public
        // surface, like the in-memory and Redis stores, is not IDisposable.
        await using var command = CreateCommand(connection, dialect.CreateTableSql(quotedTable));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        schemaEnsured = true;
    }

    private DbCommand CreateCommand(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Reads a UTC <see cref="DateTimeOffset"/> from <paramref name="reader"/>. SQL Server returns a
    /// <c>datetimeoffset</c> directly; Npgsql maps <c>timestamptz</c> to a <see cref="DateTime"/> with
    /// <see cref="DateTimeKind.Utc"/>, so both are normalised to a UTC offset here.
    /// </summary>
    private static DateTimeOffset ReadUtc(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException(
                $"Expected a timestamp at ordinal {ordinal} but read {value?.GetType().Name ?? "null"}."),
        };
    }
}

/// <summary>The bare bound-parameter names the store sets, shared with the dialect's SQL text.</summary>
internal static class RelationalParameters
{
    public const string Resource = "resource";
    public const string Holder = "holder";
    public const string TtlSeconds = "ttl_seconds";
}
