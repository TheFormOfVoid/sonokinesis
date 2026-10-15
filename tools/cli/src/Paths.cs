using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Beatform.Cli;

/// <summary>Where the tool finds the script library and the documentation.</summary>
public static class Paths
{
    private static IEnumerable<string> Roots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var d = new DirectoryInfo(start);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (seen.Add(d.FullName)) yield return d.FullName;
        }
    }

    private static string Find(string envVar, Func<string, string> candidate)
    {
        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return Path.GetFullPath(env);
        foreach (var r in Roots())
        {
            var c = candidate(r);
            if (c != null && Directory.Exists(c)) return c;
        }
        return null;
    }

    /// <summary>The built-in script library (library/ with attacks, effects, patterns), or null.</summary>
    public static string Library => library ??= Find("BEATFORM_LIBRARY", r =>
    {
        var p = Path.Combine(r, "library");
        return Directory.Exists(Path.Combine(p, "patterns")) ? p : null;
    });
    private static string library;

    /// <summary>The documentation folder (docs/), or null.</summary>
    public static string Docs => docs ??= Find("BEATFORM_DOCS", r =>
    {
        var p = Path.Combine(r, "docs");
        return File.Exists(Path.Combine(p, "scripting.md")) ? p : null;
    });
    private static string docs;

    /// <summary>The level format specification, or null.</summary>
    public static string Spec
    {
        get
        {
            foreach (var r in Roots())
            {
                foreach (var rel in new[] { Path.Combine("spec", "level-format.md"), Path.Combine("docs", "level-format.md") })
                {
                    var p = Path.Combine(r, rel);
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }
    }

    /// <summary>Library scripts by name: path and the folder they sit in (attacks, effects, patterns).</summary>
    public static Dictionary<string, (string Path, string Category)> LibraryScripts()
    {
        var res = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        if (Library == null) return res;
        foreach (var f in Directory.GetFiles(Library, "*.js", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            res[Path.GetFileNameWithoutExtension(f)] = (f, Path.GetFileName(Path.GetDirectoryName(f)));
        return res;
    }
}
