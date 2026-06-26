namespace Moongazing.OrionBeacon.Stores.Relational;

/// <summary>
/// Options for <see cref="RelationalLeaseStore"/>: which engine the connection talks to, the table
/// the leader rows live in, and the per-command timeout. Every candidate competing for the same
/// leadership must point at the same database and table so they contend over the same rows.
/// </summary>
public sealed class RelationalLeaseStoreOptions
{
    /// <summary>
    /// The relational engine the supplied connection talks to. This selects the SQL dialect for the
    /// atomic acquire-or-renew and release statements; there is no autodetection, so it must match the
    /// connection. There is no usable default: the value starts at
    /// <see cref="RelationalProvider.Unspecified"/> and the store rejects that at construction, so the
    /// engine is a required, deliberate choice rather than a silently-defaulted one that could target the
    /// wrong dialect. The <c>AddOrionBeaconPostgresStore</c> and <c>AddOrionBeaconSqlServerStore</c>
    /// registration helpers set it for you.
    /// </summary>
    public RelationalProvider Provider { get; set; } = RelationalProvider.Unspecified;

    /// <summary>
    /// The unquoted table name that holds one leader row per resource. Defaults to
    /// <c>orionbeacon_leases</c>. The name is validated to a conservative identifier shape and quoted
    /// for the engine before it is used, because a table identifier cannot be a bound parameter.
    /// </summary>
    public string TableName { get; set; } = "orionbeacon_leases";

    /// <summary>
    /// The timeout applied to every command the store runs. Bounds a network or server hang, not lease
    /// contention; contention is one atomic statement that either wins, renews, or is denied. Default
    /// 30 seconds.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
