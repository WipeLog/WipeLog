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

using Wipelog.App.ViewModels;
using Wipelog.Core.Detection;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class ClipListItemViewModelTests
{
    private static ClipRecord MakeClip(
        ContentType type,
        string displayName = "Clip",
        string? dungeon = null,
        string? instance = null,
        string? difficulty = null,
        int? keystoneLevel = null,
        bool success = true,
        string? playerName = null,
        string? playerClass = null,
        string? playerSpec = null)
    {
        var start = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        return new ClipRecord(
            Id: 1,
            VideoPath: "/x.mp4",
            SidecarPath: "/x.json",
            ContentType: type,
            DisplayName: displayName,
            Difficulty: difficulty,
            EncounterId: null,
            KeystoneLevel: keystoneLevel,
            Success: success,
            StartedAt: start,
            EndedAt: start.AddSeconds(600),
            DurationSeconds: 600,
            FileSizeBytes: 1,
            Favorite: false,
            IndexedAt: start,
            PlayerName: playerName,
            PlayerClass: playerClass,
            PlayerSpec: playerSpec,
            InstanceName: instance,
            DungeonName: dungeon);
    }

    [Fact]
    public void M_plus_dungeon_or_encounter_prefers_dungeon_name()
    {
        var vm = new ClipListItemViewModel(
            MakeClip(ContentType.MythicPlus, "Ara-Kara +18",
                dungeon: "Ara-Kara, City of Echoes"),
            Array.Empty<string>());
        Assert.Equal("Ara-Kara, City of Echoes", vm.DungeonOrEncounter);
    }

    [Fact]
    public void Raid_dungeon_or_encounter_falls_back_through_instance_to_display_name()
    {
        var withInstance = new ClipListItemViewModel(
            MakeClip(ContentType.RaidEncounter, "Ulgrax",
                instance: "Nerub-ar Palace"),
            Array.Empty<string>());
        Assert.Equal("Nerub-ar Palace", withInstance.DungeonOrEncounter);

        var noInstance = new ClipListItemViewModel(
            MakeClip(ContentType.RaidEncounter, "Ulgrax"),
            Array.Empty<string>());
        Assert.Equal("Ulgrax", noInstance.DungeonOrEncounter);
    }

    [Fact]
    public void M_plus_result_text_uses_timed_depleted_wording()
    {
        var timed = new ClipListItemViewModel(
            MakeClip(ContentType.MythicPlus, success: true),
            Array.Empty<string>());
        Assert.Equal("Timed", timed.ResultText);

        var depleted = new ClipListItemViewModel(
            MakeClip(ContentType.MythicPlus, success: false),
            Array.Empty<string>());
        Assert.Equal("Depleted", depleted.ResultText);
    }

    [Fact]
    public void Raid_result_text_uses_kill_wipe_wording()
    {
        var kill = new ClipListItemViewModel(
            MakeClip(ContentType.RaidEncounter, success: true),
            Array.Empty<string>());
        Assert.Equal("Kill", kill.ResultText);

        var wipe = new ClipListItemViewModel(
            MakeClip(ContentType.RaidEncounter, success: false),
            Array.Empty<string>());
        Assert.Equal("Wipe", wipe.ResultText);
    }

    [Fact]
    public void Keystone_text_only_shown_for_m_plus_clips()
    {
        var mplus = new ClipListItemViewModel(
            MakeClip(ContentType.MythicPlus, keystoneLevel: 18),
            Array.Empty<string>());
        Assert.Equal("+18", mplus.KeystoneText);

        var raid = new ClipListItemViewModel(
            MakeClip(ContentType.RaidEncounter, keystoneLevel: null),
            Array.Empty<string>());
        Assert.Equal(string.Empty, raid.KeystoneText);
    }

    [Fact]
    public void Pov_text_renders_class_color_for_known_class()
    {
        var vm = new ClipListItemViewModel(
            MakeClip(ContentType.MythicPlus,
                playerName: "Aelara",
                playerClass: "Mage",
                playerSpec: "Frost"),
            Array.Empty<string>());
        Assert.Equal("Aelara · Frost Mage", vm.PovText);
        Assert.Equal("#3FC7EB", vm.ClassColorHex);
    }

    [Fact]
    public void Date_text_uses_short_form_for_current_year()
    {
        // Local-time clip from earlier today/this year — DateText is
        // "HH:mm d MMM" (e.g. "15:30 9 May"). Use a current-year date to
        // avoid the test failing as soon as we cross a New Year boundary.
        var now = DateTimeOffset.Now;
        var sampleStart = new DateTimeOffset(now.Year, 5, 9, 15, 30, 0, now.Offset);
        var formatted = ClipListItemViewModel.FormatDate(sampleStart);
        Assert.Matches(@"^\d{1,2}:\d{2}\s\d{1,2}\s[A-Za-z]{3}$", formatted);
        Assert.Contains("15:30", formatted);
        Assert.Contains("May", formatted);
    }

    [Fact]
    public void Date_text_includes_year_for_older_clips()
    {
        var fiveYearsAgo = DateTimeOffset.Now.AddYears(-5);
        var formatted = ClipListItemViewModel.FormatDate(fiveYearsAgo);
        Assert.Contains(fiveYearsAgo.Year.ToString(), formatted);
    }

    [Fact]
    public void Content_badge_uses_swords_glyph_for_m_plus_and_skull_for_raid()
    {
        var mplus = new ClipListItemViewModel(
            MakeClip(ContentType.MythicPlus, keystoneLevel: 15),
            Array.Empty<string>());
        Assert.Contains("⚔", mplus.ContentBadgeText);
        Assert.Contains("+15", mplus.ContentBadgeText);

        var raid = new ClipListItemViewModel(
            MakeClip(ContentType.RaidEncounter, difficulty: "Mythic"),
            Array.Empty<string>());
        Assert.Contains("💀", raid.ContentBadgeText);
        Assert.Contains("Mythic", raid.ContentBadgeText);
    }
}
