
// =====================================================================
// STATS
// =====================================================================
internal static partial class Program
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, int> _counts = new();

    public static void Inc(string key)
    {
        lock (_lock)
        {
            if (!_counts.ContainsKey(key)) _counts[key] = 0;
            _counts[key]++;
        }
    }

    public static IReadOnlyDictionary<string, int> Snapshot()
    {
        lock (_lock) { return new Dictionary<string, int>(_counts); }
    }
}
