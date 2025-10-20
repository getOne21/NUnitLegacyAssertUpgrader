namespace NUnitLegacyAssertUpgrader;

// =====================================================================
// CLI OPTIONS
// =====================================================================
internal record CliOptions(
    bool DryRun,
    bool Backup,
    bool SinceGit,
    bool Stats,
    bool ShowHelp,
    bool WriteUntilClean,
    string? ReportPath)
{
    public CliOptions() : this(
        false, // DryRun
        false, // Backup
        false, // SinceGit
        false, // Stats
        false, // ShowHelp
        false, // WriteUntilClean
        null   // ReportPath
    )
    {
    }

    public string Root { get; private set; } = ".";
    public bool DryRun { get; private set; } = DryRun;
    public bool Backup { get; private set; } = Backup;
    public bool SinceGit { get; private set; } = SinceGit;
    public bool Stats { get; private set; } = Stats;
    public bool ShowHelp { get; private set; } = ShowHelp;
    public int Workers { get; private set; } = Math.Max(1, Environment.ProcessorCount);
    public List<string> IncludeGlobs { get; } = new();
    public List<string> ExcludeGlobs { get; } = new();
    public int MaxPasses { get; private set; } = 5;
    public string? ReportPath { get; private set; } = ReportPath;

    // Fix: Initialize backing field from constructor parameter
    public bool WriteUntilClean { get; private set; } = WriteUntilClean;

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        if (args.Length > 0) o.Root = args[0];
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--dry-run": o.DryRun = true; break;
                case "--backup": o.Backup = true; break;
                case "--since-git": o.SinceGit = true; break;
                case "--stats": o.Stats = true; break;
                case "--help": o.ShowHelp = true; break;

                case "--write-until-clean": o.WriteUntilClean = true; break;
                case "--report":
                    if (i + 1 < args.Length) o.ReportPath = args[++i];
                    break;

                case "--workers":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var w))
                    { o.Workers = Math.Max(1, w); i++; }
                    break;

                case "--include":
                    if (i + 1 < args.Length) { o.IncludeGlobs.AddRange(SplitGlobs(args[++i])); }
                    break;

                case "--exclude":
                    if (i + 1 < args.Length) { o.ExcludeGlobs.AddRange(SplitGlobs(args[++i])); }
                    break;

                default:
                    Console.WriteLine($"Unknown option: {a}");
                    o.ShowHelp = true;
                    break;
            }
        }
        return o;
    }

    private static string[] SplitGlobs(string s)
        => s.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
}