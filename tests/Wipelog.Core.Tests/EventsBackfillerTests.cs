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
using Wipelog.Daemon;
using Xunit;

namespace Wipelog.Core.Tests;

public class EventsBackfillerTests
{
    private const string V2RaidSidecar = """
        {
          "version": 2,
          "contentType": "RaidEncounter",
          "displayName": "Imperator Averzian",
          "difficulty": "Mythic",
          "encounterId": 2902,
          "success": false,
          "startedAt": "2026-05-10T17:00:00+00:00",
          "endedAt": "2026-05-10T17:02:00+00:00",
          "durationSeconds": 120,
          "videoFile": "imperator.mp4"
        }
        """;

    [Fact]
    public async Task Writes_events_sidecar_for_clip_when_log_covers_window()
    {
        using var output = new TempDirectory();
        using var logs = new TempDirectory();
        var ct = TestContext.Current.CancellationToken;

        // Clip + sidecar without an events sidecar.
        await File.WriteAllTextAsync(Path.Combine(output.Path, "imperator.json"), V2RaidSidecar, ct);
        await File.WriteAllTextAsync(Path.Combine(output.Path, "imperator.mp4"), "fake", ct);

        // Log covers the clip window with one player death + one preceding damage event.
        var logLines = string.Join('\n',
            "5/10/2026 17:00:30.000  SPELL_DAMAGE,Creature-X,\"Imperator Averzian\",0xa48,0x0,Player-1-FROSTY,\"Frostytånegl-Stormscale-EU\",0x511,0x0,12345,\"Cleave\",1,250000,0,1,0,0,0,nil,nil,nil,nil",
            "5/10/2026 17:00:30.500  UNIT_DIED,0000000000000000,nil,0x80000000,0x80000000,Player-1-FROSTY,\"Frostytånegl-Stormscale-EU\",0x511,0x0");
        await File.WriteAllTextAsync(
            Path.Combine(logs.Path, "WoWCombatLog-051026_170000.txt"), logLines, ct);

        // Pin TZ to UTC so the log-line timestamps line up with the sidecar's
        // absolute UTC times regardless of the host machine's zone.
        var backfiller = new EventsBackfiller(
            NullLogger<EventsBackfiller>.Instance,
            new FakeClock(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero)));
        var result = await backfiller.RunAsync(output.Path, logs.Path, "WoWCombatLog-*.txt", ct);

        Assert.Equal(1, result.Written);
        var eventsPath = Path.Combine(output.Path, "imperator.events.json");
        Assert.True(File.Exists(eventsPath));
        var json = await File.ReadAllTextAsync(eventsPath, ct);
        Assert.Contains("\"kind\": \"PlayerDeath\"", json);
        Assert.Contains("\"playerName\": \"Frostytånegl-Stormscale-EU\"", json);
        Assert.Contains("\"killedBySpell\": \"Cleave\"", json);
    }

    [Fact]
    public async Task Skips_clip_when_no_log_data_in_window()
    {
        using var output = new TempDirectory();
        using var logs = new TempDirectory();
        var ct = TestContext.Current.CancellationToken;

        await File.WriteAllTextAsync(Path.Combine(output.Path, "imperator.json"), V2RaidSidecar, ct);
        await File.WriteAllTextAsync(Path.Combine(output.Path, "imperator.mp4"), "fake", ct);
        // Log file exists but only has events from a different day.
        await File.WriteAllTextAsync(
            Path.Combine(logs.Path, "WoWCombatLog-old.txt"),
            "5/9/2026 12:00:00.000  ENCOUNTER_START,2902,\"Boss\",16,20,2657\n", ct);

        // Pin TZ to UTC so the log-line timestamps line up with the sidecar's
        // absolute UTC times regardless of the host machine's zone.
        var backfiller = new EventsBackfiller(
            NullLogger<EventsBackfiller>.Instance,
            new FakeClock(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero)));
        var result = await backfiller.RunAsync(output.Path, logs.Path, "WoWCombatLog-*.txt", ct);

        Assert.Equal(0, result.Written);
        Assert.Equal(1, result.Skipped);
        Assert.False(File.Exists(Path.Combine(output.Path, "imperator.events.json")));
    }

    [Fact]
    public async Task Force_overwrites_existing_events_sidecar()
    {
        using var output = new TempDirectory();
        using var logs = new TempDirectory();
        var ct = TestContext.Current.CancellationToken;

        await File.WriteAllTextAsync(Path.Combine(output.Path, "imperator.json"), V2RaidSidecar, ct);
        await File.WriteAllTextAsync(Path.Combine(output.Path, "imperator.mp4"), "fake", ct);
        // Stub events sidecar — represents an "old" file with wrong/missing data.
        var eventsPath = Path.Combine(output.Path, "imperator.events.json");
        await File.WriteAllTextAsync(eventsPath, "{\"stub\":true}", ct);

        // Log covers the clip window with one player death + kill-blow tick.
        var logLines = string.Join('\n',
            "5/10/2026 17:00:30.000  SPELL_DAMAGE,Creature-X,\"Imperator Averzian\",0xa48,0x0,Player-1-FROSTY,\"Frostytånegl-Stormscale-EU\",0x511,0x0,12345,\"Cleave\",1,250000,0,1,0,0,0,nil,nil,nil,nil",
            "5/10/2026 17:00:30.500  UNIT_DIED,0000000000000000,nil,0x80000000,0x80000000,Player-1-FROSTY,\"Frostytånegl-Stormscale-EU\",0x511,0x0");
        await File.WriteAllTextAsync(
            Path.Combine(logs.Path, "WoWCombatLog-051026_170000.txt"), logLines, ct);

        var backfiller = new EventsBackfiller(
            NullLogger<EventsBackfiller>.Instance,
            new FakeClock(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero)));
        var result = await backfiller.RunAsync(
            output.Path, logs.Path, "WoWCombatLog-*.txt", ct, force: true);

        Assert.Equal(1, result.Written);
        Assert.Equal(1, result.Overwritten);
        var json = await File.ReadAllTextAsync(eventsPath, ct);
        Assert.DoesNotContain("\"stub\"", json);
        Assert.Contains("\"kind\": \"PlayerDeath\"", json);
        Assert.Contains("\"killedBySpell\": \"Cleave\"", json);
    }

    [Fact]
    public async Task Skips_clips_that_already_have_events_sidecar()
    {
        using var output = new TempDirectory();
        using var logs = new TempDirectory();
        var ct = TestContext.Current.CancellationToken;

        await File.WriteAllTextAsync(Path.Combine(output.Path, "imperator.json"), V2RaidSidecar, ct);
        await File.WriteAllTextAsync(Path.Combine(output.Path, "imperator.mp4"), "fake", ct);
        // Pre-existing events sidecar — backfiller must NOT touch it.
        var existingPath = Path.Combine(output.Path, "imperator.events.json");
        await File.WriteAllTextAsync(existingPath, "{\"existing\":true}", ct);

        // Pin TZ to UTC so the log-line timestamps line up with the sidecar's
        // absolute UTC times regardless of the host machine's zone.
        var backfiller = new EventsBackfiller(
            NullLogger<EventsBackfiller>.Instance,
            new FakeClock(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero)));
        var result = await backfiller.RunAsync(output.Path, logs.Path, "*.txt", ct);

        Assert.Equal(0, result.Written);
        Assert.Equal("{\"existing\":true}", await File.ReadAllTextAsync(existingPath, ct));
    }
}
