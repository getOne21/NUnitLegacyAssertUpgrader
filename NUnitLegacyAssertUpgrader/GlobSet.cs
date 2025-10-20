using Microsoft.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

internal partial class Program
{
    // =====================================================================
    // MINI GLOB ENGINE (simple **/* support)
    // =====================================================================
    private class GlobSet
    {
        private readonly List<Regex> _patterns = new();

        private GlobSet(IEnumerable<string> globs)
        {
            foreach (var g in globs) _patterns.Add(new Regex("^" + RegexEscapeGlob(g) + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled));
        }

        public static GlobSet Parse(IEnumerable<string> globs) => new(globs);

        public bool IsMatch(string path)
        {
            var norm = path.Replace('\\', '/');
            return _patterns.Any(p => p.IsMatch(norm));
        }

        private static string RegexEscapeGlob(string glob)
        {
            var sb = new StringBuilder();
            var g = glob.Replace('\\', '/');

            for (int i = 0; i < g.Length; i++)
            {
                var c = g[i];
                if (c == '*')
                {
                    if (i + 1 < g.Length && g[i + 1] == '*')
                    {
                        sb.Append(".*"); i++;
                    }
                    else sb.Append("[^/]*");
                }
                else if (c == '?') sb.Append(".");
                else if ("+()^$.{}!|[]".Contains(c)) sb.Append('\\').Append(c);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
