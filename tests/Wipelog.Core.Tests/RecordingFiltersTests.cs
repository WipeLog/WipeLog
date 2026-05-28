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

using Wipelog.Core.Detection;
using Wipelog.Core.Preferences;
using Xunit;

namespace Wipelog.Core.Tests;

public class RecordingFiltersTests
{
    // WoW difficulty IDs (see DifficultyMap): 14=Normal, 15=Heroic, 16=Mythic, 17=LFR.
    private const int Heroic = 15;
    private const int Mythic = 16;

    [Fact]
    public void Default_filters_allow_everything()
    {
        var f = new RecordingFilters();
        Assert.True(f.AllowsRaid(Heroic, out _));
        Assert.True(f.AllowsRaid(Mythic, out _));
        Assert.True(f.AllowsMythicPlus(2, out _));
        Assert.True(f.AllowsBattleground(out _));
    }

    [Fact]
    public void RecordRaids_false_rejects_raid_but_allows_mythic_plus()
    {
        var f = new RecordingFilters { RecordRaids = false };
        Assert.False(f.AllowsRaid(Mythic, out var reason));
        Assert.Equal("RecordRaids is disabled", reason);
        Assert.True(f.AllowsMythicPlus(5, out _));
    }

    [Fact]
    public void RaidDifficulties_excluding_mythic_rejects_mythic_allows_heroic()
    {
        var f = new RecordingFilters
        {
            RaidDifficulties = new[]
            {
                RaidDifficulty.LFR, RaidDifficulty.Normal, RaidDifficulty.Heroic,
            },
        };
        Assert.False(f.AllowsRaid(Mythic, out var reason));
        Assert.Equal("RaidDifficulties does not include Mythic", reason);
        Assert.True(f.AllowsRaid(Heroic, out _));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(12, true)]
    public void MinKeystoneLevel_gates_runs_below_threshold(int level, bool expected)
    {
        var f = new RecordingFilters { MinKeystoneLevel = 10 };
        Assert.Equal(expected, f.AllowsMythicPlus(level, out _));
    }

    [Fact]
    public void RecordBattlegrounds_false_rejects_battleground()
    {
        var f = new RecordingFilters { RecordBattlegrounds = false };
        Assert.False(f.AllowsBattleground(out var reason));
        Assert.Equal("RecordBattlegrounds is disabled", reason);
    }

    [Fact]
    public void Empty_difficulty_set_with_raids_on_allows_all_difficulties()
    {
        var f = new RecordingFilters { RaidDifficulties = Array.Empty<RaidDifficulty>() };
        Assert.True(f.AllowsRaid(Mythic, out _));
        Assert.True(f.AllowsRaid(Heroic, out _));
    }

    [Fact]
    public void Unknown_difficulty_id_is_allowed()
    {
        var f = new RecordingFilters
        {
            RaidDifficulties = new[] { RaidDifficulty.Mythic },
        };
        // Difficulty 8 (M+ dungeon) doesn't map to a raid tier — don't filter it.
        Assert.True(f.AllowsRaid(8, out _));
        Assert.True(f.AllowsRaid(null, out _));
    }

    [Fact]
    public void Null_keystone_level_is_allowed()
    {
        var f = new RecordingFilters { MinKeystoneLevel = 20 };
        Assert.True(f.AllowsMythicPlus(null, out _));
    }

    [Fact]
    public void Roundtrips_through_preferences_file_with_difficulties_as_strings()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        var filters = new RecordingFilters
        {
            RecordRaids = true,
            RecordMythicPlus = false,
            RecordBattlegrounds = false,
            RaidDifficulties = new[]
            {
                RaidDifficulty.Heroic, RaidDifficulty.Mythic,
            },
            MinKeystoneLevel = 8,
        };
        new UserPreferencesService(path).Save(new UserPreferences { RecordingFilters = filters });

        // Difficulties serialize as a string array, not integers.
        var json = File.ReadAllText(path);
        Assert.Contains("\"Heroic\"", json);
        Assert.Contains("\"Mythic\"", json);

        var read = new UserPreferencesService(path).Load().RecordingFilters;
        Assert.True(read.RecordRaids);
        Assert.False(read.RecordMythicPlus);
        Assert.False(read.RecordBattlegrounds);
        Assert.Equal(8, read.MinKeystoneLevel);
        Assert.Equal(
            new HashSet<RaidDifficulty> { RaidDifficulty.Heroic, RaidDifficulty.Mythic },
            read.RaidDifficulties.ToHashSet());
    }

    [Fact]
    public void Missing_recordingFilters_block_loads_defaults()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        // A preferences file that predates the recordingFilters block.
        File.WriteAllText(path, "{ \"notifyOnClipSaved\": false }");

        var read = new UserPreferencesService(path).Load().RecordingFilters;
        Assert.True(read.RecordRaids);
        Assert.True(read.RecordMythicPlus);
        Assert.True(read.RecordBattlegrounds);
        Assert.Equal(2, read.MinKeystoneLevel);
        Assert.Equal(4, read.RaidDifficulties.Count);
    }
}
