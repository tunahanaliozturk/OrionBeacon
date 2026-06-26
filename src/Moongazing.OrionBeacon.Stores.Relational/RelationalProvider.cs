namespace Moongazing.OrionBeacon.Stores.Relational;

/// <summary>
/// The relational backend a <see cref="RelationalLeaseStore"/> talks to. The acquire-or-renew and
/// release statements differ between engines (PostgreSQL's <c>INSERT ... ON CONFLICT ... RETURNING</c>
/// versus SQL Server's <c>MERGE ... OUTPUT</c>), and the two also disagree on how a positional
/// parameter is written, so the store selects a <see cref="RelationalDialect"/> from this value.
/// </summary>
public enum RelationalProvider
{
    /// <summary>PostgreSQL, reached through <c>Npgsql</c>.</summary>
    Postgres,

    /// <summary>Microsoft SQL Server, reached through <c>Microsoft.Data.SqlClient</c>.</summary>
    SqlServer,
}
