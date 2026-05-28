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

using Wipelog.App.Services;
using Xunit;

namespace Wipelog.Core.Tests;

public class WowLogDetectorTests
{
    /// <summary>
    /// Builds a synthetic WoW directory under <paramref name="prefix"/> matching
    /// the layout the real launchers leave on disk:
    /// <c>{prefix}/drive_c/Program Files (x86)/World of Warcraft/_retail_/Logs/WoWCombatLog-...txt</c>
    /// </summary>
    private static string PlantLogFile(
        string prefix, string logFileName,
        string suffix = "Program Files (x86)",
        DateTime? touch = null)
    {
        var logsDir = Path.Combine(prefix, "drive_c", suffix, "World of Warcraft", "_retail_", "Logs");
        Directory.CreateDirectory(logsDir);
        var file = Path.Combine(logsDir, logFileName);
        File.WriteAllText(file, "");
        if (touch is { } t) File.SetLastWriteTimeUtc(file, t);
        return logsDir;
    }

    [Fact]
    public void Detect_returns_empty_when_no_search_roots_exist()
    {
        using var dir = new TempDirectory();
        var detector = new WowLogDetector(new[] { Path.Combine(dir.Path, "does-not-exist") });
        Assert.Empty(detector.Detect());
    }

    [Fact]
    public void Detect_finds_logs_under_a_root_that_is_itself_a_wine_prefix()
    {
        // Mirrors the raw-Wine case (~/.wine): the search root is itself
        // a single Wine prefix, not a parent of multiple prefixes.
        using var dir = new TempDirectory();
        var winePrefix = dir.Path;
        var expected = PlantLogFile(winePrefix, "WoWCombatLog-090623_193045.txt");

        var detector = new WowLogDetector(new[] { winePrefix });

        var hits = detector.Detect();
        var hit = Assert.Single(hits);
        Assert.Equal(expected, hit.Path);
        Assert.Equal(1, hit.LogFileCount);
        Assert.NotNull(hit.NewestLogAt);
    }

    [Fact]
    public void Detect_finds_logs_under_a_root_that_holds_child_prefixes()
    {
        // Mirrors Lutris / Heroic / Bottles: the search root is a parent
        // directory whose children are individual Wine prefixes.
        using var dir = new TempDirectory();
        var root = Path.Combine(dir.Path, "Games");
        Directory.CreateDirectory(root);
        var gamePrefix = Path.Combine(root, "world-of-warcraft");
        var expected = PlantLogFile(gamePrefix, "WoWCombatLog-090623_193045.txt");

        var detector = new WowLogDetector(new[] { root });

        var hits = detector.Detect();
        var hit = Assert.Single(hits);
        Assert.Equal(expected, hit.Path);
    }

    [Fact]
    public void Detect_accepts_both_Program_Files_and_Program_Files_x86()
    {
        // Modern 64-bit clients sometimes drop into "Program Files" without
        // the (x86) suffix; both must be recognised.
        using var dir = new TempDirectory();
        var noX86 = Path.Combine(dir.Path, "no-x86");
        PlantLogFile(noX86, "WoWCombatLog-1.txt", suffix: "Program Files");

        var detector = new WowLogDetector(new[] { noX86 });
        Assert.Single(detector.Detect());
    }

    [Fact]
    public void Detect_orders_results_by_newest_log_first()
    {
        // Two candidates - the one with the more recent log activity is
        // the live install; the older one is a stale prefix the user
        // forgot to clean up.
        using var dir = new TempDirectory();
        var stale = Path.Combine(dir.Path, "stale-prefix");
        PlantLogFile(stale, "WoWCombatLog-old.txt",
            touch: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var live = Path.Combine(dir.Path, "live-prefix");
        PlantLogFile(live, "WoWCombatLog-new.txt",
            touch: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var detector = new WowLogDetector(new[] { dir.Path });

        var hits = detector.Detect();
        Assert.Equal(2, hits.Count);
        Assert.Contains("live-prefix", hits[0].Path, StringComparison.Ordinal);
        Assert.Contains("stale-prefix", hits[1].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_does_not_match_a_prefix_with_no_combat_log_files()
    {
        using var dir = new TempDirectory();
        // Plant the directory layout but no log files inside.
        var prefix = Path.Combine(dir.Path, "empty-prefix");
        var logsDir = Path.Combine(prefix,
            "drive_c", "Program Files (x86)", "World of Warcraft", "_retail_", "Logs");
        Directory.CreateDirectory(logsDir);

        var detector = new WowLogDetector(new[] { dir.Path });
        Assert.Empty(detector.Detect());
    }

    [Fact]
    public void Detect_dedupes_when_the_same_directory_is_reached_via_two_roots()
    {
        // Lutris' default root (~/Games) and its managed prefix dir
        // (~/.local/share/lutris/runners/winewow) can both point at the
        // same prefix. Make sure we don't list it twice.
        using var dir = new TempDirectory();
        var prefix = Path.Combine(dir.Path, "wow-prefix");
        PlantLogFile(prefix, "WoWCombatLog-1.txt");

        // Two roots that both lead to the same prefix through the
        // children-enumeration path.
        var detector = new WowLogDetector(new[] { dir.Path, dir.Path });
        Assert.Single(detector.Detect());
    }

    [Fact]
    public void Detect_counts_multiple_log_files_in_a_single_prefix()
    {
        using var dir = new TempDirectory();
        var prefix = Path.Combine(dir.Path, "wow-prefix");
        PlantLogFile(prefix, "WoWCombatLog-1.txt");
        PlantLogFile(prefix, "WoWCombatLog-2.txt");
        PlantLogFile(prefix, "WoWCombatLog-3.txt");

        var detector = new WowLogDetector(new[] { dir.Path });
        var hit = Assert.Single(detector.Detect());
        Assert.Equal(3, hit.LogFileCount);
    }
}
