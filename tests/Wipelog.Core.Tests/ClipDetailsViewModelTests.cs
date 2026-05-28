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

using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.App.Services;
using Wipelog.App.ViewModels;
using Wipelog.Core.Data;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class ClipDetailsViewModelTests
{
    private sealed class StubEventsLoader : IClipEventsLoader
    {
        public Func<string, CancellationToken, Task<TimelineEventDocument?>> Handler { get; set; }
            = (_, _) => Task.FromResult<TimelineEventDocument?>(null);

        public Task<TimelineEventDocument?> LoadAsync(string videoPath, CancellationToken ct) =>
            Handler(videoPath, ct);
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message,
            string confirmLabel = "Delete", string cancelLabel = "Cancel") =>
            Task.FromResult(true);
        public Task<string?> SaveFilePickerAsync(string suggestedName, string filterName,
            IReadOnlyList<string> extensions, CancellationToken ct) =>
            Task.FromResult<string?>(null);
        public Task<T> ShowProgressAsync<T>(string title,
            Func<IProgress<ProgressUpdate>, CancellationToken, Task<T>> work) =>
            work(new Progress<ProgressUpdate>(_ => { }), CancellationToken.None);
    }

    private sealed class StubArchiver : IClipArchiver
    {
        public Task<ArchiveResult> ArchiveAsync(IReadOnlyList<ClipRecord> clips,
            string outputPath, IProgress<ArchiveProgress>? progress, CancellationToken ct) =>
            Task.FromResult(new ArchiveResult(0, 0, 0, outputPath, false));
    }

    private static (ClipDetailsViewModel Vm, StubEventsLoader Loader, TempDirectory Dir) NewVm()
    {
        var dir = new TempDirectory();
        var db = new ClipDatabase(Path.Combine(dir.Path, "clips.db"), NullLogger<ClipDatabase>.Instance);
        var repo = new ClipRepository(db);
        var filters = new ClipFiltersViewModel();
        var dialogs = new StubDialogService();
        var archiver = new StubArchiver();
        var prefs = new Wipelog.Core.Preferences.UserPreferencesService(Path.Combine(dir.Path, "prefs.json"));
        var list = new ClipListViewModel(repo, filters, dialogs, archiver, prefs, new SukiUI.Toasts.SukiToastManager(), NullLogger<ClipListViewModel>.Instance);
        var tags = new TagsEditorViewModel(repo);
        var loader = new StubEventsLoader();
        var vm = new ClipDetailsViewModel(list, repo, dialogs, tags, loader,
            NullLogger<ClipDetailsViewModel>.Instance);
        return (vm, loader, dir);
    }

    private static ClipListItemViewModel MakeItem(
        ContentType type,
        string displayName = "Clip",
        string? difficulty = null,
        int? keystoneLevel = null,
        int? encounterId = null,
        string? dungeonName = null,
        string? instanceName = null,
        string? arenaBracket = null,
        string? notes = null,
        long id = 1,
        bool success = true,
        double? bossHpPercent = null,
        int? keyUpgradeLevel = null)
    {
        var rec = new ClipRecord(
            Id: id,
            VideoPath: "/x.mp4",
            SidecarPath: "/x.json",
            ContentType: type,
            DisplayName: displayName,
            Difficulty: difficulty,
            EncounterId: encounterId,
            KeystoneLevel: keystoneLevel,
            Success: success,
            StartedAt: DateTimeOffset.UtcNow,
            EndedAt: DateTimeOffset.UtcNow.AddMinutes(5),
            DurationSeconds: 300,
            FileSizeBytes: 1024,
            Favorite: false,
            IndexedAt: DateTimeOffset.UtcNow,
            InstanceName: instanceName,
            DungeonName: dungeonName,
            ArenaBracket: arenaBracket,
            Notes: notes,
            BossHpPercent: bossHpPercent,
            KeyUpgradeLevel: keyUpgradeLevel);
        return new ClipListItemViewModel(rec, Array.Empty<string>());
    }

    // ── Per-type Is* flag projection ─────────────────────────────────────────

    [Theory]
    [InlineData(ContentType.RaidEncounter, true, false, false, false, false)]
    [InlineData(ContentType.MythicPlus,    false, true, false, false, false)]
    [InlineData(ContentType.Arena,         false, false, true, false, false)]
    [InlineData(ContentType.SoloShuffle,   false, false, false, true, false)]
    [InlineData(ContentType.Battleground,  false, false, false, false, true)]
    [InlineData(ContentType.RatedBattleground, false, false, false, false, true)]
    public void Per_type_flags_only_one_is_true_per_ContentType(
        ContentType type, bool raid, bool mplus, bool arena, bool shuffle, bool bg)
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(type);
            Assert.Equal(raid,    vm.IsRaid);
            Assert.Equal(mplus,   vm.IsMythicPlus);
            Assert.Equal(arena,   vm.IsArena);
            Assert.Equal(shuffle, vm.IsSoloShuffle);
            Assert.Equal(bg,      vm.IsBattleground);
        }
    }

    [Fact]
    public void No_selection_means_no_type_flags()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            Assert.False(vm.IsRaid);
            Assert.False(vm.IsMythicPlus);
            Assert.False(vm.IsArena);
            Assert.False(vm.IsSoloShuffle);
            Assert.False(vm.IsBattleground);
            Assert.False(vm.HasSelection);
        }
    }

    // ── BadgeText / ResultText per content type ──────────────────────────────

    [Fact]
    public void BadgeText_for_M_plus_includes_keystone_level()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.MythicPlus, keystoneLevel: 18);
            Assert.Equal("M+ +18", vm.BadgeText);
        }
    }

    [Fact]
    public void BadgeText_for_Raid_includes_difficulty()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.RaidEncounter, difficulty: "Mythic");
            Assert.Equal("Raid Mythic", vm.BadgeText);
        }
    }

    [Fact]
    public void BadgeText_for_Arena_uses_bracket()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.Arena, arenaBracket: "Arena3v3");
            Assert.Equal("Arena3v3", vm.BadgeText);
        }
    }

    [Fact]
    public void BadgeText_for_SoloShuffle_is_label_only()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.SoloShuffle, arenaBracket: "Rated Solo Shuffle");
            Assert.Equal("Solo Shuffle", vm.BadgeText);
        }
    }

    [Theory]
    [InlineData(ContentType.Battleground,      "Battleground")]
    [InlineData(ContentType.RatedBattleground, "Rated BG")]
    public void BadgeText_for_BG_variants(ContentType type, string expected)
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(type);
            Assert.Equal(expected, vm.BadgeText);
        }
    }

    [Theory]
    [InlineData(ContentType.MythicPlus, true,  "Timed")]
    [InlineData(ContentType.MythicPlus, false, "Depleted")]
    [InlineData(ContentType.RaidEncounter, true,  "Kill")]
    [InlineData(ContentType.RaidEncounter, false, "Wipe")]
    public void ResultText_for_PvE_reflects_success(ContentType type, bool success, string expected)
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(type, success: success);
            Assert.Equal(expected, vm.ResultText);
        }
    }

    [Theory]
    [InlineData(1, "Timed +1")]
    [InlineData(2, "Timed +2")]
    [InlineData(3, "Timed +3")]
    public void ResultText_for_M_plus_appends_key_upgrade_level(int level, string expected)
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.MythicPlus, success: true, keyUpgradeLevel: level);
            Assert.Equal(expected, vm.ResultText);
        }
    }

    [Fact]
    public void ResultText_for_M_plus_omits_plus_when_upgrade_level_unknown()
    {
        // Unknown-par dungeon (DungeonTimers miss) — the detector left
        // KeyUpgradeLevel null; UI should fall back to plain "Timed".
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.MythicPlus, success: true, keyUpgradeLevel: null);
            Assert.Equal("Timed", vm.ResultText);
        }
    }

    [Fact]
    public void ResultText_for_M_plus_uses_Depleted_regardless_of_level()
    {
        // KeyUpgradeLevel = 0 would only appear on a depleted run. Result
        // pill stays "Depleted" — "+0" would be misleading.
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.MythicPlus, success: false, keyUpgradeLevel: 0);
            Assert.Equal("Depleted", vm.ResultText);
        }
    }

    [Theory]
    [InlineData(true,  "Win")]
    [InlineData(false, "Loss")]
    public void ResultText_for_Arena_reflects_success(bool success, string expected)
    {
        // Arena win/loss is resolved at finalize via ArenaMatchEnd.WinningFaction
        // vs the player's faction; Success is meaningful for this type.
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.Arena, success: success);
            Assert.Equal(expected, vm.ResultText);
        }
    }

    [Theory]
    [InlineData(ContentType.SoloShuffle)]
    [InlineData(ContentType.Battleground)]
    [InlineData(ContentType.RatedBattleground)]
    public void ResultText_for_other_PvP_types_stays_neutral_complete(ContentType type)
    {
        // Solo Shuffle has per-round structure (no single winner); BG / RBG
        // have no clean win/loss signal in the log we trust yet.
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(type);
            Assert.Equal("Complete", vm.ResultText);
        }
    }

    // ── Per-type metadata projections ────────────────────────────────────────

    [Fact]
    public void Bracket_row_only_shows_for_Arena_or_SoloShuffle()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.Arena, arenaBracket: "Arena3v3");
            Assert.True(vm.HasBracket);
            Assert.Equal("Arena3v3", vm.BracketText);

            vm.Selected = MakeItem(ContentType.SoloShuffle, arenaBracket: "Rated Solo Shuffle");
            Assert.True(vm.HasBracket);

            vm.Selected = MakeItem(ContentType.RaidEncounter, arenaBracket: null);
            Assert.False(vm.HasBracket);
        }
    }

    [Fact]
    public void Map_row_only_shows_for_Battleground()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.Battleground, displayName: "Warsong Gulch");
            Assert.True(vm.HasMap);
            Assert.Equal("Warsong Gulch", vm.MapText);

            vm.Selected = MakeItem(ContentType.RatedBattleground, displayName: "Arathi Basin");
            Assert.True(vm.HasMap);

            vm.Selected = MakeItem(ContentType.MythicPlus, displayName: "The Stonevault");
            Assert.False(vm.HasMap);
        }
    }

    [Fact]
    public void Dungeon_row_only_shows_for_M_plus()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.MythicPlus, dungeonName: "Ara-Kara, City of Echoes");
            Assert.True(vm.HasDungeon);
            Assert.Equal("Ara-Kara, City of Echoes", vm.DungeonText);

            vm.Selected = MakeItem(ContentType.RaidEncounter, dungeonName: "Ignored");
            Assert.False(vm.HasDungeon);
        }
    }

    [Fact]
    public void Instance_row_only_shows_for_Raid_with_an_instance()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.RaidEncounter, instanceName: "Manaforge Omega");
            Assert.True(vm.HasInstance);
            Assert.Equal("Manaforge Omega", vm.InstanceText);

            vm.Selected = MakeItem(ContentType.RaidEncounter, instanceName: null);
            Assert.False(vm.HasInstance);

            vm.Selected = MakeItem(ContentType.Arena, instanceName: "Should be ignored");
            Assert.False(vm.HasInstance);
        }
    }

    // ── Comp / death chip strip (restored from #3) ───────────────────────────

    private static TimelineEventDocument MakeDoc(int tanks = 0, int healers = 0, int dps = 0, int deaths = 0)
    {
        var start = new DateTimeOffset(2026, 5, 10, 17, 0, 0, TimeSpan.Zero);
        var combatants = new List<CombatantRecord>();
        int n = 0;
        for (int i = 0; i < tanks; i++)
            combatants.Add(new CombatantRecord($"Player-T-{n++}", 250, "Death Knight", "Blood", SpecRole.Tank));
        for (int i = 0; i < healers; i++)
            combatants.Add(new CombatantRecord($"Player-H-{n++}", 257, "Priest", "Holy", SpecRole.Healer));
        for (int i = 0; i < dps; i++)
            combatants.Add(new CombatantRecord($"Player-D-{n++}", 64, "Mage", "Frost", SpecRole.Dps));
        var events = new List<TimelineEvent>();
        for (int i = 0; i < deaths; i++)
            events.Add(new PlayerDeathEvent(1000 * (i + 1), $"Player-X-{i}", "Foo", null, null, null));
        return new TimelineEventDocument(
            Version: 2,
            RecordingStartedAt: start,
            RecordingEndedAt: start.AddMinutes(5),
            Events: events)
        {
            Combatants = combatants,
        };
    }

    [Fact]
    public async Task Selecting_clip_loads_comp_counts_from_events_sidecar()
    {
        var (vm, loader, dir) = NewVm();
        using (dir)
        {
            loader.Handler = (_, _) =>
                Task.FromResult<TimelineEventDocument?>(MakeDoc(tanks: 2, healers: 3, dps: 5, deaths: 10));

            vm.Selected = MakeItem(ContentType.RaidEncounter);
            await vm.EventsLoadTask;

            Assert.True(vm.HasComp);
            Assert.True(vm.HasAnyBadge);
            Assert.Equal(2, vm.TankCount);
            Assert.Equal(3, vm.HealerCount);
            Assert.Equal(5, vm.DpsCount);
            Assert.Equal(10, vm.DeathCount);
        }
    }

    [Fact]
    public async Task Missing_events_sidecar_keeps_chip_strip_hidden()
    {
        var (vm, loader, dir) = NewVm();
        using (dir)
        {
            loader.Handler = (_, _) => Task.FromResult<TimelineEventDocument?>(null);

            vm.Selected = MakeItem(ContentType.RaidEncounter);
            await vm.EventsLoadTask;

            Assert.False(vm.HasComp);
            Assert.False(vm.HasDeaths);
            Assert.False(vm.HasAnyBadge);
        }
    }

    // ── Notes (per-clip free-form text) ──────────────────────────────────────

    [Fact]
    public void Selecting_a_clip_loads_its_notes_into_the_editor()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.RaidEncounter, notes: "0:42 missed Cinderbrew");
            Assert.Equal("0:42 missed Cinderbrew", vm.NotesText);
        }
    }

    [Fact]
    public void Selecting_a_clip_with_null_notes_yields_empty_string()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.RaidEncounter, notes: null);
            Assert.Equal(string.Empty, vm.NotesText);
        }
    }

    [Fact]
    public void Clearing_selection_clears_notes()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.RaidEncounter, notes: "hi");
            Assert.Equal("hi", vm.NotesText);
            vm.Selected = null;
            Assert.Equal(string.Empty, vm.NotesText);
        }
    }

    [Fact]
    public async Task CommitNotesAsync_persists_changes_and_refreshes_record()
    {
        // Real ClipRepository via NewVm's temp DB — write a clip, select it,
        // edit notes, commit, re-fetch and check the column.
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var db = new ClipDatabase(Path.Combine(dir.Path, "clips.db"), NullLogger<ClipDatabase>.Instance);
            var repo = new ClipRepository(db);
            var clip = new ClipRecord(
                Id: 0, VideoPath: "/n.mp4", SidecarPath: "/n.json",
                ContentType: ContentType.RaidEncounter, DisplayName: "Boss",
                Difficulty: "Mythic", EncounterId: 1, KeystoneLevel: null, Success: true,
                StartedAt: DateTimeOffset.UtcNow, EndedAt: DateTimeOffset.UtcNow.AddMinutes(5),
                DurationSeconds: 300, FileSizeBytes: 1, Favorite: false,
                IndexedAt: DateTimeOffset.UtcNow);
            var id = await repo.InsertOrUpdateClipAsync(clip, ct);
            var stored = await repo.GetClipAsync(id, ct);

            vm.Selected = new ClipListItemViewModel(stored!, Array.Empty<string>());
            vm.NotesText = "new note";
            await vm.CommitNotesAsync();

            var fetched = await repo.GetClipAsync(id, ct);
            Assert.Equal("new note", fetched!.Notes);
            // Local cached record is also refreshed so re-selecting won't
            // show the pre-edit value.
            Assert.Equal("new note", vm.Selected!.Record.Notes);
        }
    }

    [Fact]
    public async Task CommitNotesAsync_with_unchanged_text_is_a_noop()
    {
        // The "current == draft" guard skips a pointless round-trip; verify
        // by selecting a clip with notes and committing without editing.
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var db = new ClipDatabase(Path.Combine(dir.Path, "clips.db"), NullLogger<ClipDatabase>.Instance);
            var repo = new ClipRepository(db);
            var clip = new ClipRecord(
                Id: 0, VideoPath: "/n.mp4", SidecarPath: "/n.json",
                ContentType: ContentType.RaidEncounter, DisplayName: "Boss",
                Difficulty: "Mythic", EncounterId: 1, KeystoneLevel: null, Success: true,
                StartedAt: DateTimeOffset.UtcNow, EndedAt: DateTimeOffset.UtcNow.AddMinutes(5),
                DurationSeconds: 300, FileSizeBytes: 1, Favorite: false,
                IndexedAt: DateTimeOffset.UtcNow,
                Notes: "starting note");
            var id = await repo.InsertOrUpdateClipAsync(clip, ct);
            var stored = await repo.GetClipAsync(id, ct);

            vm.Selected = new ClipListItemViewModel(stored!, Array.Empty<string>());
            // NotesText matches stored value verbatim
            await vm.CommitNotesAsync();

            var fetched = await repo.GetClipAsync(id, ct);
            Assert.Equal("starting note", fetched!.Notes);
        }
    }

    // ── MM:SS timestamp extraction for the chip strip ────────────────────────

    [Fact]
    public void Empty_notes_produce_no_timestamps()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.NotesText = string.Empty;
            Assert.Empty(vm.NoteTimestamps);
            Assert.False(vm.HasNoteTimestamps);
        }
    }

    [Fact]
    public void Single_MM_SS_token_yields_one_timestamp()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.NotesText = "0:42 missed Cinderbrew";
            var ts = Assert.Single(vm.NoteTimestamps);
            Assert.Equal("0:42", ts.Label);
            Assert.Equal(TimeSpan.FromSeconds(42), ts.Offset);
            Assert.True(vm.HasNoteTimestamps);
        }
    }

    [Fact]
    public void Multi_digit_minutes_parse_correctly()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.NotesText = "12:34 phase 3 pull";
            var ts = Assert.Single(vm.NoteTimestamps);
            Assert.Equal(new TimeSpan(0, 12, 34), ts.Offset);
        }
    }

    [Fact]
    public void Multiple_timestamps_preserve_insertion_order()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.NotesText = "2:18 wrong stack\n0:42 missed interrupt\n5:00 enrage";
            Assert.Equal(3, vm.NoteTimestamps.Count);
            Assert.Equal("2:18", vm.NoteTimestamps[0].Label);
            Assert.Equal("0:42", vm.NoteTimestamps[1].Label);
            Assert.Equal("5:00", vm.NoteTimestamps[2].Label);
        }
    }

    [Fact]
    public void Duplicate_timestamps_deduplicate_to_a_single_chip()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.NotesText = "0:42 first miss\nlater: 0:42 same beat\n0:42 again";
            var ts = Assert.Single(vm.NoteTimestamps);
            Assert.Equal("0:42", ts.Label);
        }
    }

    [Fact]
    public void Invalid_seconds_60_or_more_are_dropped()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            // 12:99 isn't a real time; chip strip should ignore it.
            vm.NotesText = "12:99 nonsense\n3:00 valid";
            var ts = Assert.Single(vm.NoteTimestamps);
            Assert.Equal("3:00", ts.Label);
        }
    }

    [Fact]
    public void Notes_without_MM_SS_yield_empty_list()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.NotesText = "just some thoughts, no timestamps";
            Assert.Empty(vm.NoteTimestamps);
            Assert.False(vm.HasNoteTimestamps);
        }
    }

    [Fact]
    public void Editing_notes_recomputes_timestamps()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.NotesText = "0:42 first";
            Assert.Single(vm.NoteTimestamps);

            vm.NotesText = "0:42 first\n1:30 second";
            Assert.Equal(2, vm.NoteTimestamps.Count);

            vm.NotesText = "no timestamps left";
            Assert.Empty(vm.NoteTimestamps);
            Assert.False(vm.HasNoteTimestamps);
        }
    }

    [Fact]
    public void HasNoteTimestamps_PropertyChanged_fires_on_recompute()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.NotesText = "0:42";
            Assert.Contains(nameof(ClipDetailsViewModel.NoteTimestamps), changed);
            Assert.Contains(nameof(ClipDetailsViewModel.HasNoteTimestamps), changed);
        }
    }

    [Fact]
    public async Task CommitNotesAsync_clears_notes_when_text_is_whitespace_only()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var db = new ClipDatabase(Path.Combine(dir.Path, "clips.db"), NullLogger<ClipDatabase>.Instance);
            var repo = new ClipRepository(db);
            var clip = new ClipRecord(
                Id: 0, VideoPath: "/n.mp4", SidecarPath: "/n.json",
                ContentType: ContentType.RaidEncounter, DisplayName: "Boss",
                Difficulty: "Mythic", EncounterId: 1, KeystoneLevel: null, Success: true,
                StartedAt: DateTimeOffset.UtcNow, EndedAt: DateTimeOffset.UtcNow.AddMinutes(5),
                DurationSeconds: 300, FileSizeBytes: 1, Favorite: false,
                IndexedAt: DateTimeOffset.UtcNow,
                Notes: "to be cleared");
            var id = await repo.InsertOrUpdateClipAsync(clip, ct);
            var stored = await repo.GetClipAsync(id, ct);

            vm.Selected = new ClipListItemViewModel(stored!, Array.Empty<string>());
            vm.NotesText = "   \n  ";
            await vm.CommitNotesAsync();

            var fetched = await repo.GetClipAsync(id, ct);
            Assert.Null(fetched!.Notes);
            Assert.Null(vm.Selected!.Record.Notes);
        }
    }

    [Fact]
    public void Wipe_hp_row_visible_for_raid_wipe_with_boss_hp_data()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.RaidEncounter, success: false, bossHpPercent: 27.3);

            Assert.True(vm.HasWipeHp);
            Assert.Equal("Wiped at 27%", vm.WipeHpText);
        }
    }

    [Fact]
    public void Wipe_hp_uses_one_decimal_under_ten_percent()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.RaidEncounter, success: false, bossHpPercent: 0.4);

            Assert.True(vm.HasWipeHp);
            Assert.Equal("Wiped at 0.4%", vm.WipeHpText);
        }
    }

    [Fact]
    public void Wipe_hp_row_hidden_for_raid_kills()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.RaidEncounter, success: true, bossHpPercent: 0);
            Assert.False(vm.HasWipeHp);
        }
    }

    [Fact]
    public void Wipe_hp_row_hidden_for_mplus_even_when_data_present()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            vm.Selected = MakeItem(ContentType.MythicPlus, success: false, bossHpPercent: 50);
            Assert.False(vm.HasWipeHp);
        }
    }

    [Fact]
    public void Wipe_hp_row_hidden_when_boss_hp_unknown()
    {
        var (vm, _, dir) = NewVm();
        using (dir)
        {
            // Pre-feature raid wipe with no BossHpPercent on the row.
            vm.Selected = MakeItem(ContentType.RaidEncounter, success: false, bossHpPercent: null);
            Assert.False(vm.HasWipeHp);
            Assert.Equal(string.Empty, vm.WipeHpText);
        }
    }
}
