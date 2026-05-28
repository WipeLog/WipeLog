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

using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class RecorderControllerIdentityTests
{
    private static DateTimeOffset T(int s) =>
        new DateTimeOffset(2026, 5, 9, 19, 0, 0, TimeSpan.Zero).AddSeconds(s);

    private sealed class CapturingSink : IClipRecorderSink
    {
        public List<ClipFinalization> Records { get; } = new();
        public Task RecordAsync(ClipFinalization clip, CancellationToken ct)
        {
            Records.Add(clip);
            return Task.CompletedTask;
        }
    }

    private const string MineFlags = "0x511";
    private const string RaidFlags = "0x514";

    private static UnknownEvent PrefixedSourceEvent(
        string sourceGuid,
        string sourceName,
        int second,
        string sourceFlags = RaidFlags) =>
        new(T(second), "SPELL_CAST_SUCCESS",
            new[] { sourceGuid, sourceName, sourceFlags, "0x0", "00000000", "nil", "0x0", "0x0" });

    [Fact]
    public async Task Raid_kill_with_player_name_resolves_identity_and_instance_name()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var sink = new CapturingSink();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance, sink,
            playerName: "Frostytanegl");

        var tct = TestContext.Current.CancellationToken;
        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        // Encounter on Liberation of Undermine (instanceId 2657 in RaidInstances).
        await ch.Writer.WriteAsync(new EncounterStart(T(0), 2902, "Ulgrax", 16, 20, 2657), TestContext.Current.CancellationToken);
        // Fully-qualified source name with MINE bit; realm + region split off in storage.
        await ch.Writer.WriteAsync(PrefixedSourceEvent("Player-1-FROSTY", "Frostytanegl-Stormscale-EU", 1, MineFlags), TestContext.Current.CancellationToken);
        // CombatantInfo for that GUID with Frost Mage spec.
        await ch.Writer.WriteAsync(new CombatantInfo(T(2), "Player-1-FROSTY", 64), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new EncounterEnd(T(120), 2902, "Ulgrax", 16, 20, Success: true), TestContext.Current.CancellationToken);
        ch.Writer.Complete();

        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        Assert.Single(sink.Records);
        var rec = sink.Records[0];
        Assert.Equal("Frostytanegl", rec.PlayerName);
        Assert.Equal("Stormscale", rec.PlayerRealm);
        Assert.Equal("EU", rec.PlayerRegion);
        Assert.Equal("Mage", rec.PlayerClass);
        Assert.Equal("Frost", rec.PlayerSpec);
        Assert.Equal("Liberation of Undermine", rec.InstanceName);
        Assert.Null(rec.DungeonName);
    }

    [Fact]
    public async Task M_plus_run_sets_dungeon_name_to_display_name()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var sink = new CapturingSink();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance, sink,
            playerName: "Frostytanegl");

        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        await ch.Writer.WriteAsync(new ChallengeModeStart(T(0), "The Stonevault", 2290, 505, 12, new[] { 9 }), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(PrefixedSourceEvent("Player-1-FROSTY", "Frostytanegl", 1, MineFlags), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new CombatantInfo(T(2), "Player-1-FROSTY", 64), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new ChallengeModeEnd(T(1800), 2290, Success: true, KeystoneLevel: 12, Duration: TimeSpan.FromMinutes(30)), TestContext.Current.CancellationToken);
        ch.Writer.Complete();

        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        var rec = sink.Records.Single();
        Assert.Equal("The Stonevault", rec.DungeonName);
        Assert.Null(rec.InstanceName);
        Assert.Equal("Frost", rec.PlayerSpec);
    }

    [Fact]
    public async Task Recording_succeeds_without_player_name_and_identity_fields_stay_null()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var sink = new CapturingSink();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance, sink,
            playerName: null);

        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        await ch.Writer.WriteAsync(new ChallengeModeStart(T(0), "The Stonevault", 2290, 505, 12, new[] { 9 }), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new ChallengeModeEnd(T(60), 2290, Success: false, KeystoneLevel: 12, Duration: TimeSpan.FromMinutes(1)), TestContext.Current.CancellationToken);
        ch.Writer.Complete();

        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        var rec = sink.Records.Single();
        Assert.Null(rec.PlayerName);
        Assert.Null(rec.PlayerClass);
        Assert.Equal("The Stonevault", rec.DungeonName); // dungeon name is still set from displayName
    }

    [Fact]
    public async Task Player_name_with_no_matching_source_event_falls_back_to_auto_detect()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var sink = new CapturingSink();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance, sink,
            playerName: "WrongName");

        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        await ch.Writer.WriteAsync(new EncounterStart(T(0), 2902, "Boss", 16, 20, 2657), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(PrefixedSourceEvent("Player-1-FROSTY", "Frostytanegl", 1), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new CombatantInfo(T(2), "Player-1-FROSTY", 64), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new EncounterEnd(T(60), 2902, "Boss", 16, 20, Success: true), TestContext.Current.CancellationToken);
        ch.Writer.Complete();

        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        var rec = sink.Records.Single();
        // Mismatched hint falls through to auto-detect; FROSTY is the only player GUID.
        Assert.Equal("Frostytanegl", rec.PlayerName);
        Assert.Equal("Mage", rec.PlayerClass);
        Assert.Equal("Frost", rec.PlayerSpec);
    }

    [Fact]
    public async Task Diacritic_mismatch_in_hint_still_resolves_unicode_name()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var sink = new CapturingSink();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance, sink,
            playerName: "Frostytanegl"); // ASCII hint

        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        await ch.Writer.WriteAsync(new EncounterStart(T(0), 2902, "Boss", 16, 20, 2657), TestContext.Current.CancellationToken);
        // Real game name uses å.
        await ch.Writer.WriteAsync(PrefixedSourceEvent("Player-1-FROSTY", "Frostytånegl", 1), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new CombatantInfo(T(2), "Player-1-FROSTY", 64), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new EncounterEnd(T(60), 2902, "Boss", 16, 20, Success: true), TestContext.Current.CancellationToken);
        ch.Writer.Complete();

        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        var rec = sink.Records.Single();
        Assert.Equal("Frostytånegl", rec.PlayerName); // unicode preserved
        Assert.Equal("Mage", rec.PlayerClass);
        Assert.Equal("Frost", rec.PlayerSpec);
    }

    [Fact]
    public async Task Stop_writes_events_sidecar_with_player_death()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var sink = new CapturingSink();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance, sink,
            playerName: null);

        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        await ch.Writer.WriteAsync(new EncounterStart(T(0), 2902, "Boss", 16, 20, 2657), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(PrefixedSourceEvent("Player-1-FROSTY", "Frostytanegl", 1, MineFlags), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new CombatantInfo(T(2), "Player-1-FROSTY", 64), TestContext.Current.CancellationToken);
        // Kill blow + death of an ally raid member 30s into the pull.
        await ch.Writer.WriteAsync(new DamageEvent(T(29), "Creature-X", "Boss", "Player-2-ALLY", "Allyname", 12345, "Cleave", 250000), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new UnitDied(T(30), "Player-2-ALLY", "Allyname-Stormrage-EU"), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new EncounterEnd(T(60), 2902, "Boss", 16, 20, Success: true), TestContext.Current.CancellationToken);
        ch.Writer.Complete();

        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        var rec = sink.Records.Single();
        Assert.NotNull(rec.EventsSidecarPath);
        Assert.True(File.Exists(rec.EventsSidecarPath));

        var json = await File.ReadAllTextAsync(rec.EventsSidecarPath!, TestContext.Current.CancellationToken);
        Assert.Contains("\"version\": 2", json);
        Assert.Contains("\"kind\": \"PlayerDeath\"", json);
        Assert.Contains("\"playerName\": \"Allyname-Stormrage-EU\"", json);
        Assert.Contains("\"killedBySource\": \"Boss\"", json);
        Assert.Contains("\"killedBySpell\": \"Cleave\"", json);
    }

    [Fact]
    public async Task Raid_with_busy_tank_resolves_recording_player_via_mine_flag()
    {
        // End-to-end version of the real-world bug: in an LFR pull the tank
        // generates more source events than the recording player. Without the
        // MINE-flag check, the resolver picks the tank by frequency. With it,
        // the recording player wins.
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var sink = new CapturingSink();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance, sink,
            playerName: null);

        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        await ch.Writer.WriteAsync(new EncounterStart(T(0), 2902, "Imperator Averzian", 17, 20, 2657), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new CombatantInfo(T(1), "Player-1-FROSTY", 64), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new CombatantInfo(T(1), "Player-2-TANK", 73), TestContext.Current.CancellationToken);
        // Tank with raid flags: many source events, no MINE.
        for (int i = 0; i < 50; i++)
        {
            await ch.Writer.WriteAsync(
                PrefixedSourceEvent("Player-2-TANK", "Protwarrior-Doomhammer-EU", second: 2 + i, sourceFlags: RaidFlags),
                TestContext.Current.CancellationToken);
        }
        // Recording player with MINE flag: fewer events, but unmistakable.
        for (int i = 0; i < 5; i++)
        {
            await ch.Writer.WriteAsync(
                PrefixedSourceEvent("Player-1-FROSTY", "Frostytånegl", second: 60 + i, sourceFlags: MineFlags),
                TestContext.Current.CancellationToken);
        }
        await ch.Writer.WriteAsync(new EncounterEnd(T(120), 2902, "Imperator Averzian", 17, 20, Success: false), TestContext.Current.CancellationToken);
        ch.Writer.Complete();

        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        var rec = sink.Records.Single();
        Assert.Equal("Frostytånegl", rec.PlayerName);
        Assert.Equal("Mage", rec.PlayerClass);
        Assert.Equal("Frost", rec.PlayerSpec);
    }

    [Theory]
    [InlineData(0, 0, true,  "_win.mp4")]   // Horde player, Horde wins → win
    [InlineData(1, 1, true,  "_win.mp4")]   // Alliance player, Alliance wins → win
    [InlineData(0, 1, false, "_loss.mp4")]  // Horde player, Alliance wins → loss
    [InlineData(1, 0, false, "_loss.mp4")]  // Alliance player, Horde wins → loss
    public async Task Arena_finalize_resolves_win_loss_from_factions(
        int playerFaction, int winningFaction, bool expectedSuccess, string expectedSuffix)
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var sink = new CapturingSink();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance, sink,
            playerName: "Frostytanegl");

        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        await ch.Writer.WriteAsync(new ArenaMatchStart(T(0), InstanceId: 572, Bracket: "Arena3v3", MatchType: "Ranked"), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(PrefixedSourceEvent("Player-1-FROSTY", "Frostytanegl", 1, MineFlags), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new CombatantInfo(T(2), "Player-1-FROSTY", SpecId: 64, Faction: playerFaction), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new ArenaMatchEnd(T(300), WinningFaction: winningFaction, Duration: TimeSpan.FromSeconds(300)), TestContext.Current.CancellationToken);
        ch.Writer.Complete();

        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        var rec = sink.Records.Single();
        Assert.Equal(ContentType.Arena, rec.Type);
        Assert.Equal(expectedSuccess, rec.Success);
        Assert.EndsWith(expectedSuffix, rec.VideoPath);
    }

    [Fact]
    public async Task Arena_finalize_falls_back_to_complete_when_player_faction_missing()
    {
        // If COMBATANT_INFO never lands (e.g. truncated log), faction can't be
        // resolved — the controller leaves the detector's placeholder alone.
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var sink = new CapturingSink();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance, sink,
            playerName: "Frostytanegl");

        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        await ch.Writer.WriteAsync(new ArenaMatchStart(T(0), 572, "Arena3v3", "Ranked"), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(PrefixedSourceEvent("Player-1-FROSTY", "Frostytanegl", 1, MineFlags), TestContext.Current.CancellationToken);
        // No CombatantInfo → no faction on identity.
        await ch.Writer.WriteAsync(new ArenaMatchEnd(T(300), WinningFaction: 1, Duration: TimeSpan.FromSeconds(300)), TestContext.Current.CancellationToken);
        ch.Writer.Complete();

        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        var rec = sink.Records.Single();
        Assert.True(rec.Success); // detector placeholder survives
        Assert.EndsWith("_complete.mp4", rec.VideoPath);
    }
}
