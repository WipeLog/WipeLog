// Wipelog — Linux-native World of Warcraft gameplay recorder.
// Copyright (C) 2026 Patrik - WipeLog-dev
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace Wipelog.App.Services;

/// <summary>
/// One detected candidate combat-log directory. <see cref="LogFileCount"/>
/// is how many <c>WoWCombatLog-*.txt</c> files were found inside; the GUI
/// sorts by this descending (highest is the active install) and surfaces
/// it so the user can tell stale prefixes from the live one. </summary>
public sealed record DetectedLogPath(
    string Path,
    int LogFileCount,
    DateTimeOffset? NewestLogAt);

/// <summary>
/// Discovers WoW combat-log directories under the well-known Linux
/// launcher prefixes (Lutris, Heroic, Bottles, Faugus, raw Wine). Used by the
/// first-run onboarding banner in Settings: the user clicks "Detect WoW
/// logs", we offer the candidates, and they pick one with a click.
///
/// <para>
/// The detection is deterministic - we don't recursively walk the user's
/// home looking for combat logs (slow and noisy). Instead we know the
/// terminal suffix every WoW install has (<c>drive_c/.../World of Warcraft/_retail_/Logs</c>),
/// enumerate the per-launcher prefix dirs, and check each combination.
/// </para>
/// </summary>
public interface IWowLogDetector
{
    IReadOnlyList<DetectedLogPath> Detect();
}

public sealed class WowLogDetector : IWowLogDetector
{
    // The directory tree under any Wine prefix containing a WoW install
    // ends with one of these two paths (32-bit installer dropped them
    // under "Program Files (x86)" historically; modern 64-bit clients use
    // the no-x86 variant on some prefixes).
    private static readonly string[] WowLogSuffixes =
    {
        Path.Combine("drive_c", "Program Files (x86)", "World of Warcraft", "_retail_", "Logs"),
        Path.Combine("drive_c", "Program Files",       "World of Warcraft", "_retail_", "Logs"),
    };

    private readonly IReadOnlyList<string> _searchRoots;

    /// <summary>
    /// Build a detector with the standard Linux WoW launcher search roots.
    /// Exposed without the parameter so production code (DI) doesn't have to
    /// know the path list.
    /// </summary>
    public WowLogDetector() : this(DefaultSearchRoots())
    {
    }

    /// <summary>
    /// Construct with an explicit search-root list. Used by tests to point
    /// the detector at a synthetic temp directory containing the expected
    /// WoW directory layout.
    /// </summary>
    public WowLogDetector(IReadOnlyList<string> searchRoots)
    {
        _searchRoots = searchRoots;
    }

    /// <summary>
    /// Default per-launcher prefix dirs. Each entry is a directory whose
    /// *children* are individual Wine prefixes — the detector enumerates
    /// one level deep and checks each child + every direct path entry.
    /// Raw Wine (<c>~/.wine</c>) is itself a single prefix so it's added
    /// directly rather than as a parent of prefixes.
    /// </summary>
    public static IReadOnlyList<string> DefaultSearchRoots()
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = string.IsNullOrEmpty(xdgConfig) ? Path.Combine(home, ".config") : xdgConfig;
        var roots = new List<string>
        {
            // Lutris default install location — one subdir per game.
            Path.Combine(home, "Games"),
            // Lutris managed prefix dir.
            Path.Combine(home, ".local", "share", "lutris", "runners", "winewow"),
            // Heroic prefixes.
            Path.Combine(home, "Games", "Heroic", "Prefixes", "default"),
            // Bottles, flatpak (the common form).
            Path.Combine(home, ".var", "app", "com.usebottles.bottles", "data", "bottles", "bottles"),
            // Bottles, non-flatpak.
            Path.Combine(home, ".local", "share", "bottles", "bottles"),
            // Faugus Launcher default — one subdir per prefix (~/Faugus/<name>).
            Path.Combine(home, "Faugus"),
            // Raw Wine — itself a single prefix, not a parent of them.
            Path.Combine(home, ".wine"),
        };
        foreach (var faugusRoot in FaugusConfiguredPrefixRoots(home, configHome))
        {
            if (!roots.Contains(faugusRoot, StringComparer.Ordinal)) roots.Add(faugusRoot);
        }
        return roots;
    }

    /// <summary>
    /// Faugus lets the user move its prefix root; the choice is stored as
    /// <c>default-prefix</c> in <c>faugus-launcher/config.json</c> (native
    /// config dir, or the flatpak's private one). Returns every configured
    /// root found; missing/unreadable configs contribute nothing.
    /// </summary>
    internal static IEnumerable<string> FaugusConfiguredPrefixRoots(string home, string configHome)
    {
        var configs = new[]
        {
            Path.Combine(configHome, "faugus-launcher", "config.json"),
            Path.Combine(home, ".var", "app", "io.github.Faugus.faugus-launcher", "config", "faugus-launcher", "config.json"),
        };
        foreach (var config in configs)
        {
            string? root = null;
            try
            {
                if (!File.Exists(config)) continue;
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(config));
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("default-prefix", out var prop)
                    && prop.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    root = prop.GetString();
                }
            }
            catch
            {
                // Malformed / unreadable config — fall back to the default root.
            }
            if (!string.IsNullOrWhiteSpace(root)) yield return root;
        }
    }

    public IReadOnlyList<DetectedLogPath> Detect()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<DetectedLogPath>();
        foreach (var root in _searchRoots)
        {
            foreach (var prefix in CandidatePrefixes(root))
            {
                foreach (var suffix in WowLogSuffixes)
                {
                    var candidate = Path.Combine(prefix, suffix);
                    if (!TryDetectAt(candidate, out var hit)) continue;
                    if (!seen.Add(candidate)) continue;
                    found.Add(hit);
                }
            }
        }
        // Most-active candidate first so the user picks the live prefix
        // when several are present.
        found.Sort((a, b) =>
        {
            var byTime = Nullable.Compare(b.NewestLogAt, a.NewestLogAt);
            if (byTime != 0) return byTime;
            return b.LogFileCount.CompareTo(a.LogFileCount);
        });
        return found;
    }

    private static IEnumerable<string> CandidatePrefixes(string root)
    {
        if (!Directory.Exists(root)) yield break;
        // The root itself might be a single prefix (e.g. ~/.wine).
        yield return root;
        // ...and so might each of its direct children (Lutris, Heroic,
        // Bottles all use a single nesting level for individual prefixes).
        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(root); }
        catch { yield break; }
        foreach (var child in children) yield return child;
    }

    private static bool TryDetectAt(string logDir, out DetectedLogPath hit)
    {
        hit = default!;
        try
        {
            if (!Directory.Exists(logDir)) return false;
            var matches = Directory.GetFiles(logDir, "WoWCombatLog-*.txt");
            if (matches.Length == 0) return false;
            DateTimeOffset? newest = null;
            foreach (var f in matches)
            {
                var t = new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero);
                if (newest is null || t > newest) newest = t;
            }
            hit = new DetectedLogPath(logDir, matches.Length, newest);
            return true;
        }
        catch
        {
            // Permission denied / transient FS error — skip the candidate.
            return false;
        }
    }
}
