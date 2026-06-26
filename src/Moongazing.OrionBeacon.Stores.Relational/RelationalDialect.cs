using System.Text.RegularExpressions;

namespace Moongazing.OrionBeacon.Stores.Relational;

/// <summary>
/// The per-engine SQL the <see cref="RelationalLeaseStore"/> runs. There are two correctness-critical
/// statements and they read differently on each engine, so each <see cref="RelationalProvider"/> has
/// its own dialect: the acquire-or-renew upsert (PostgreSQL <c>INSERT ... ON CONFLICT ... RETURNING</c>
/// versus SQL Server <c>MERGE ... OUTPUT</c>), the fencing-checked release, and the one-time schema
/// create. The engine clock (<c>now()</c> / <c>SYSUTCDATETIME()</c>) decides liveness, so candidates
/// with skewed wall clocks still agree on whether a lease is live.
/// </summary>
/// <remarks>
/// <para>
/// The contract the SQL enforces, in one atomic statement so two candidates can never both acquire:
/// </para>
/// <list type="bullet">
/// <item>No row, or the row's lease has expired, or the caller already holds it: the statement writes
/// the caller as holder and extends the lease. The fencing token advances by exactly one when the
/// caller did not already hold a live lease (a new leadership term) and is left unchanged when the
/// caller is renewing a lease it already holds.</item>
/// <item>A different candidate holds a live lease: the statement changes nothing and returns no row,
/// which the store reads as a denial and follows with a holder lookup.</item>
/// </list>
/// <para>
/// The fencing token lives in the row and is never reset. A release does not delete the row; it sets
/// the expiry into the past (a tombstone) so the stored token survives the handover and the next
/// acquirer advances strictly past it, exactly as the in-memory and Redis stores do. The initial
/// insert race between two nodes creating the very first row is resolved by the primary key on
/// <c>resource</c>: the upsert form on both engines is atomic against a concurrent insert, so the
/// loser converts to the conditional-update branch rather than failing on a duplicate key.
/// </para>
/// </remarks>
internal abstract class RelationalDialect
{
    /// <summary>A conservative identifier shape: a letter or underscore, then letters, digits, or
    /// underscores. A table name is interpolated into DDL/DML (it cannot be a bound parameter), so it
    /// is validated to this shape and engine-quoted to keep it from being an injection vector.</summary>
    private static readonly Regex IdentifierPattern =
        new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Selects the dialect for <paramref name="provider"/>.</summary>
    /// <param name="provider">The engine the connection talks to.</param>
    /// <returns>The matching dialect.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The provider is not a known value.</exception>
    public static RelationalDialect For(RelationalProvider provider) => provider switch
    {
        RelationalProvider.Postgres => PostgresInstance,
        RelationalProvider.SqlServer => SqlServerInstance,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown relational provider."),
    };

    private static readonly RelationalDialect PostgresInstance = new PostgresDialect();
    private static readonly RelationalDialect SqlServerInstance = new SqlServerDialect();

    /// <summary>The parameter name for the resource (the contended key).</summary>
    protected const string Resource = RelationalParameters.Resource;

    /// <summary>The parameter name for the candidate identity.</summary>
    protected const string Holder = RelationalParameters.Holder;

    /// <summary>The parameter name for the lease duration, in seconds.</summary>
    protected const string TtlSeconds = RelationalParameters.TtlSeconds;

    /// <summary>
    /// Validates <paramref name="tableName"/> as a plain identifier and returns it quoted for the
    /// engine, ready to interpolate into a statement.
    /// </summary>
    /// <param name="tableName">The unquoted table name from options.</param>
    /// <returns>The engine-quoted identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is not a plain identifier.</exception>
    public string QuoteTable(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        if (!IdentifierPattern.IsMatch(tableName))
        {
            throw new ArgumentException(
                $"Table name '{tableName}' must be a plain identifier (a letter or underscore followed by letters, digits, or underscores); it is interpolated into SQL and cannot be parameterised.",
                nameof(tableName));
        }

        return QuoteIdentifier(tableName);
    }

    /// <summary>Quotes a validated identifier in the engine's own delimiter.</summary>
    protected abstract string QuoteIdentifier(string identifier);

    /// <summary>
    /// The idempotent schema-create statement for <paramref name="quotedTable"/>: a row per resource
    /// keyed by <c>resource</c>, with the holder, the fencing token, and the acquire and expiry
    /// timestamps. Safe to run repeatedly (create-if-absent).
    /// </summary>
    /// <param name="quotedTable">The engine-quoted table name from <see cref="QuoteTable"/>.</param>
    public abstract string CreateTableSql(string quotedTable);

    /// <summary>
    /// The atomic acquire-or-renew statement for <paramref name="quotedTable"/>. It returns one row
    /// when the caller now holds the lease, with columns in this order: <c>fencing_token</c> (bigint),
    /// <c>acquired_at</c> (UTC timestamp), <c>expires_at</c> (UTC timestamp), and <c>renewed</c> (a
    /// boolean: true when the caller already held a live lease and only extended it, false when a new
    /// term began). It returns no row when a different candidate holds a live lease.
    /// </summary>
    /// <param name="quotedTable">The engine-quoted table name.</param>
    public abstract string AcquireOrRenewSql(string quotedTable);

    /// <summary>
    /// The fencing-checked release for <paramref name="quotedTable"/>: tombstones the row (sets its
    /// expiry into the past) only when the caller is the current holder, leaving the fencing token in
    /// place so the next acquirer advances past it. A no-op when the caller does not hold the lease.
    /// </summary>
    /// <param name="quotedTable">The engine-quoted table name.</param>
    public abstract string ReleaseSql(string quotedTable);

    /// <summary>
    /// The holder lookup for <paramref name="quotedTable"/>, used only after a denial to report which
    /// candidate holds the live lease. Returns the holder when the row exists and its lease is live,
    /// otherwise no row (the holder released or its lease lapsed in the race between the two reads).
    /// </summary>
    /// <param name="quotedTable">The engine-quoted table name.</param>
    public abstract string CurrentHolderSql(string quotedTable);

    // -- PostgreSQL ---------------------------------------------------------------------------------

    private sealed class PostgresDialect : RelationalDialect
    {
        protected override string QuoteIdentifier(string identifier) => "\"" + identifier + "\"";

        public override string CreateTableSql(string quotedTable) => $@"
CREATE TABLE IF NOT EXISTS {quotedTable} (
    resource      text                     NOT NULL PRIMARY KEY,
    holder        text                     NOT NULL,
    fencing_token bigint                   NOT NULL,
    acquired_at   timestamptz              NOT NULL,
    expires_at    timestamptz              NOT NULL
);";

        // One statement, as a writable CTE so the pre-update token can be captured and compared to the
        // post-write token without relying on a wall-clock equality (now() is the stable transaction
        // time on Postgres, but deriving "renewed" from a token comparison is engine-robust and matches
        // the SQL Server form). The `prior` CTE reads the existing row (if any) under the same snapshot;
        // the upsert routes any existing row to ON CONFLICT DO UPDATE, whose WHERE makes the write
        // conditional:
        //   - caller already holds a LIVE lease  -> renew: keep token and acquired_at, extend expiry.
        //   - row free/expired, or it is the caller's own expired row -> acquire: token = old + 1.
        //   - a different candidate holds a live lease -> WHERE is false, no row written, no row out.
        // renewed is true exactly when a prior row existed and the token did not move (a genuine renew).
        public override string AcquireOrRenewSql(string quotedTable) => $@"
WITH prior AS (
    SELECT fencing_token FROM {quotedTable} WHERE resource = @{Resource}
),
upsert AS (
    INSERT INTO {quotedTable} (resource, holder, fencing_token, acquired_at, expires_at)
    VALUES (@{Resource}, @{Holder}, 1, now(), now() + make_interval(secs => @{TtlSeconds}))
    ON CONFLICT (resource) DO UPDATE SET
        holder = @{Holder},
        fencing_token = CASE
            WHEN {quotedTable}.holder = @{Holder} AND {quotedTable}.expires_at > now()
            THEN {quotedTable}.fencing_token
            ELSE {quotedTable}.fencing_token + 1 END,
        acquired_at = CASE
            WHEN {quotedTable}.holder = @{Holder} AND {quotedTable}.expires_at > now()
            THEN {quotedTable}.acquired_at
            ELSE now() END,
        expires_at = now() + make_interval(secs => @{TtlSeconds})
    WHERE {quotedTable}.holder = @{Holder} OR {quotedTable}.expires_at <= now()
    RETURNING fencing_token, acquired_at, expires_at
)
SELECT
    upsert.fencing_token,
    upsert.acquired_at,
    upsert.expires_at,
    (prior.fencing_token IS NOT NULL AND prior.fencing_token = upsert.fencing_token) AS renewed
FROM upsert
LEFT JOIN prior ON true;";

        public override string ReleaseSql(string quotedTable) => $@"
UPDATE {quotedTable}
SET expires_at = 'epoch'::timestamptz
WHERE resource = @{Resource} AND holder = @{Holder};";

        public override string CurrentHolderSql(string quotedTable) => $@"
SELECT holder FROM {quotedTable}
WHERE resource = @{Resource} AND expires_at > now();";
    }

    // -- SQL Server ---------------------------------------------------------------------------------

    private sealed class SqlServerDialect : RelationalDialect
    {
        protected override string QuoteIdentifier(string identifier) => "[" + identifier + "]";

        public override string CreateTableSql(string quotedTable) => $@"
IF OBJECT_ID(N'{quotedTable}', N'U') IS NULL
BEGIN
    CREATE TABLE {quotedTable} (
        resource      nvarchar(450)    NOT NULL PRIMARY KEY,
        holder        nvarchar(450)    NOT NULL,
        fencing_token bigint           NOT NULL,
        acquired_at   datetimeoffset(7) NOT NULL,
        expires_at    datetimeoffset(7) NOT NULL
    );
END;";

        // MERGE with HOLDLOCK is SQL Server's atomic upsert: HOLDLOCK takes a range lock on the key so
        // a concurrent MERGE for the same resource serialises behind it rather than both inserting.
        //   - matched, caller holds a LIVE lease           -> renew: keep token, extend expiry.
        //   - matched, free/expired or caller's expired row -> acquire: token = old + 1.
        //   - matched, a different candidate's live lease   -> the WHEN MATCHED guard is false, no write.
        //   - not matched                                   -> first acquire, token = 1.
        // OUTPUT exposes the pre-update row as `deleted` and the post-write row as `inserted`, so
        // renewed is true exactly when a row pre-existed and the token did not move (a genuine renew),
        // which is clock-independent unlike comparing SYSUTCDATETIME() to a stored timestamp.
        public override string AcquireOrRenewSql(string quotedTable) => $@"
MERGE {quotedTable} WITH (HOLDLOCK) AS target
USING (SELECT @{Resource} AS resource) AS source
ON target.resource = source.resource
WHEN MATCHED AND (target.holder = @{Holder} OR target.expires_at <= SYSUTCDATETIME()) THEN
    UPDATE SET
        holder = @{Holder},
        fencing_token = CASE
            WHEN target.holder = @{Holder} AND target.expires_at > SYSUTCDATETIME()
            THEN target.fencing_token
            ELSE target.fencing_token + 1 END,
        acquired_at = CASE
            WHEN target.holder = @{Holder} AND target.expires_at > SYSUTCDATETIME()
            THEN target.acquired_at
            ELSE SYSUTCDATETIME() END,
        expires_at = DATEADD(SECOND, @{TtlSeconds}, SYSUTCDATETIME())
WHEN NOT MATCHED THEN
    INSERT (resource, holder, fencing_token, acquired_at, expires_at)
    VALUES (@{Resource}, @{Holder}, 1, SYSUTCDATETIME(), DATEADD(SECOND, @{TtlSeconds}, SYSUTCDATETIME()))
OUTPUT
    inserted.fencing_token,
    inserted.acquired_at,
    inserted.expires_at,
    CASE
        WHEN deleted.fencing_token IS NOT NULL AND deleted.fencing_token = inserted.fencing_token
        THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS renewed;";

        public override string ReleaseSql(string quotedTable) => $@"
UPDATE {quotedTable}
SET expires_at = CAST('0001-01-01T00:00:00+00:00' AS datetimeoffset(7))
WHERE resource = @{Resource} AND holder = @{Holder};";

        public override string CurrentHolderSql(string quotedTable) => $@"
SELECT holder FROM {quotedTable}
WHERE resource = @{Resource} AND expires_at > SYSUTCDATETIME();";
    }
}
