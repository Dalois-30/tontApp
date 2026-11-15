namespace Domain.Common;

/// <summary>
/// Time source for values that take part in a hash. PostgreSQL stores timestamps with microsecond
/// precision while .NET ticks are 100ns: a hash computed on an untruncated DateTime cannot be
/// recomputed after a database round-trip. Always truncate to the microsecond.
/// </summary>
public static class Horloge
{
    public static DateTime MaintenantUtc() => TronquerMicroseconde(DateTime.UtcNow);

    public static DateTime TronquerMicroseconde(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, value.Kind);
}
