using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnitLegacyAssertUpgrader;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal partial class Program
{
    private static int Main(string[] args)
    {
        PrintBanner();

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var options = CliOptions.Parse(args);
        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        if (options.DryRun && options.WriteUntilClean)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Note: --write-until-clean is ignored in --dry-run mode.");
            Console.ResetColor();

            options = new CliOptions(
                options.DryRun,
                options.Backup,
                options.SinceGit,
                options.Stats,
                options.ShowHelp,
                options.WriteUntilClean,
                options.ReportPath);
        }

        // Gather files
        var all = options.SinceGit ? FilesFromGit(options.Root) : FilesFromDisk(options.Root);
        var filtered = ApplyIncludeExclude(all, options);
        if (filtered.Count == 0)
        {
            Console.WriteLine("No candidate files found after include/exclude filters.");
            MaybeWriteReport(options, new ConversionReport(0, 0, 0)
            {
                FilesChanged = [],
                RuleStats = []
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"Scanning {filtered.Count} files for classic NUnit asserts...");
        Console.WriteLine();

        var overallChangedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int totalProcessed = 0;
        var swOverall = Stopwatch.StartNew();

        int pass = 0;
        int changedThisPass;
        do
        {
            pass++;
            if (options.WriteUntilClean)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\nPass {pass}...");
                Console.ResetColor();
            }

            changedThisPass = RunOnePass(filtered, options, out var processedThisPass, overallChangedFiles);
            totalProcessed += processedThisPass;

            if (!options.WriteUntilClean) break;

            if (changedThisPass == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\nClean: no further changes detected.");
                Console.ResetColor();
                break;
            }

            if (pass >= options.MaxPasses)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\nReached max passes ({options.MaxPasses}). Some chained patterns may remain.");
                Console.ResetColor();
                break;
            }
        }
        while (true);

        swOverall.Stop();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\nProcessed {totalProcessed} file-visits in {swOverall.Elapsed.TotalSeconds:n1}s — upgraded {overallChangedFiles.Count} files.");
        Console.ResetColor();

        if (options.Stats)
        {
            Console.WriteLine("\nRule stats (transform → count):");
            foreach (var kv in Snapshot().OrderBy(k => k.Key))
            {
                Console.WriteLine($"  {kv.Key,-40} {kv.Value,6}");
            }
        }

        MaybeWriteReport(options, new ConversionReport(totalProcessed, overallChangedFiles.Count, swOverall.Elapsed.TotalSeconds)
        {
            FilesChanged = overallChangedFiles.OrderBy(x => x).ToList(),
            RuleStats = new Dictionary<string, int>(Snapshot())
        });

        Console.WriteLine("\n✨ Tests transformed. Logic preserved. Readability achieved.");
        Console.WriteLine("Legacy code tamed. Future devs thank you.\n");
        return 0;

        // Runs a single pass over the filtered file set. Returns number of changed files in this pass.
        int RunOnePass(List<string> files, CliOptions options, out int processedCount, HashSet<string> overallChangedFiles)
        {
            int changed = 0;
            int processed = 0;
            var progressLock = new object();
            var rewriter = new NUnitAssertRewriter();

            var sw = Stopwatch.StartNew();
            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = options.Workers },
                file =>
                {
                    var originalText = File.ReadAllText(file);
                    var tree = CSharpSyntaxTree.ParseText(originalText);
                    var root = tree.GetRoot();

                    var newRoot = rewriter.Visit(root);
                    var wrote = false;

                    if (!ReferenceEquals(root, newRoot))
                    {
                        var newText = newRoot.ToFullString();
                        if (!newText.Equals(originalText, StringComparison.Ordinal))
                        {
                            if (!options.DryRun)
                            {
                                if (options.Backup) SafeWrite(file + ".bak", originalText);
                                SafeWrite(file, newText);
                            }

                            lock (progressLock)
                            {
                                changed++;
                                overallChangedFiles.Add(file);
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"{(options.DryRun ? "[DRY]" : "[WRITE]")} {file}");
                                Console.ResetColor();
                            }
                            wrote = true;
                        }
                    }

                    lock (progressLock)
                    {
                        processed++;
                        if (processed % 25 == 0)
                        {
                            var pct = (int)((double)processed / files.Count * 100);
                            Console.Write($"\r[{new string('#', pct / 5)}{new string('-', 20 - pct / 5)}] {pct,3}%");
                        }
                    }
                });
            sw.Stop();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Pass took {sw.Elapsed.TotalSeconds:n1}s — {changed} file(s) changed.");
            Console.ResetColor();

            processedCount = processed;
            return changed;
        }

        void PrintBanner()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("             --- NUnit Legacy Assert Upgrader --- \n");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("      Legacy NUnit Classic Assert To Assert Constraint-Style \n");
            Console.ResetColor();
            Console.WriteLine("---------------------------------------------------------------");
        }

        void PrintUsage()
        {
            Console.WriteLine(@"
        Usage:
            NUnitAssertUpgrader <repo-root> [options]

        Options:
            --dry-run              Preview only (no writes)
            --backup               Create .bak files for changed sources
            --since-git            Only process files changed/untracked according to git
            --include <globs>      Comma- or space-separated globs (default: **/*.cs)
            --exclude <globs>      Comma- or space-separated globs (default: **/bin/**,**/obj/**)
            --workers <n>          Max degree of parallelism (default: CPU count)
            --stats                Print per-rule conversion counts

            --write-until-clean    Re-run passes until no further changes (max 5 passes)
            --report <path.json>   Emit a JSON report (processed, changed, file list, rule stats)

            --help                 Show this help

        Examples:
            NUnitAssertUpgrader . --backup --stats --write-until-clean --report report.json
            NUnitAssertUpgrader ..\src --since-git --include ""**/*.Tests.cs"" --exclude ""**/Generated/**""
        ");
        }

        void SafeWrite(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        List<string> FilesFromDisk(string root)
        {
            return [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))];
        }

        List<string> FilesFromGit(string root)
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            static IEnumerable<string> RunGit(string args, string cwd)
            {
                try
                {
                    var p = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "git",
                            Arguments = args,
                            WorkingDirectory = cwd,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    p.Start();
                    var lines = p.StandardOutput.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    p.WaitForExit(5000);
                    return lines;
                }
                catch { return []; }
            }

            foreach (var line in RunGit("ls-files -m", root)) list.Add(Path.GetFullPath(Path.Combine(root, line)));
            foreach (var line in RunGit("ls-files -o --exclude-standard", root)) list.Add(Path.GetFullPath(Path.Combine(root, line)));

            // Fallback to disk if git isn't available
            if (list.Count == 0) return FilesFromDisk(root);
            return list.Where(f => File.Exists(f) && f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        List<string> ApplyIncludeExclude(List<string> files, CliOptions o)
        {
            var include = GlobSet.Parse(o.IncludeGlobs.Count != 0 ? o.IncludeGlobs : new[] { "**/*.cs" });
            var exclude = GlobSet.Parse(o.ExcludeGlobs.Count != 0 ? o.ExcludeGlobs : new[] { "**/bin/**", "**/obj/**" });

            return [.. files.Where(f => include.IsMatch(f) && !exclude.IsMatch(f))];
        }

        void MaybeWriteReport(CliOptions o, ConversionReport report)
        {
            if (string.IsNullOrWhiteSpace(o.ReportPath)) return;

            try
            {
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                SafeWrite(Path.GetFullPath(o.ReportPath), json);
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"\nReport written: {Path.GetFullPath(o.ReportPath)}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Failed to write report: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}