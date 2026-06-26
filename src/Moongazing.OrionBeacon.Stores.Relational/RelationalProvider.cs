namespace Moongazing.OrionBeacon.Stores.Relational;

/// <summary>
/// The relational backend a <see cref="RelationalLeaseStore"/> talks to. The acquire-or-renew and
/// release statements differ between engines (PostgreSQL's <c>INSERT ... ON CONFLICT ... RETURNING</c>
/// versus SQL Server's <c>MERGE ... OUTPUT</c>), and the two also disagree on how a positional
/// parameter is written, so the store selects a <see cref="RelationalDialect"/> from this value.
/// </summary>
public enum RelationalProvider
{
    /// <summary>
    /// No engine chosen. The default value, present only so that leaving
    /// <see cref="RelationalLeaseStoreOptions.Provider"/> unset is a detectable error rather than
    /// silently selecting an engine: the store rejects this value at construction. Never select it
    /// deliberately.
    /// </summary>
    Unspecified = 0,

    /// <summary>PostgreSQL, reached through <c>Npgsql</c>.</summary>
    Postgres = 1,

    /// <summary>Microsoft SQL Server, reached through <c>Microsoft.Data.SqlClient</c>.</summary>
    SqlServer = 2,
}
