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
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class LibraryIndexerV2Tests
{
    private const string V1MythicPlusSidecar = """
        {
          "version": 1,
          "contentType": "MythicPlus",
          "displayName": "The Stonevault",
          "keystoneLevel": 12,
          "success": true,
          "startedAt": "2026-05-09T20:15:00+00:00",
          "endedAt": "2026-05-09T20:50:00+00:00",
          "durationSeconds": 2100,
          "videoFile": "stonevault.mp4"
        }
        """;

    private const string V2RaidSidecar = """
        {
          "version": 2,
          "contentType": "RaidEncounter",
          "displayName": "Ulgrax the Devourer",
          "difficulty": "Mythic",
          "encounterId": 2902,
          "success": true,
          "startedAt": "2026-05-09T19:43:01.122+00:00",
          "endedAt": "2026-05-09T19:47:55.890+00:00",
          "durationSeconds": 294,
          "videoFile": "ulgrax.mp4",
          "playerName": "Frostytanegl",
          "playerRealm": "Stormscale",
          "playerClass": "Mage",
          "playerSpec": "Frost",
          "instanceName": "Liberation of Undermine"
        }
        """;

    private static (LibraryIndexer Indexer, ClipRepository Repo, TempDirectory Output, TempDirectory Db) NewIndexer()
    {
        var output = new TempDirectory();
        var dbDir = new TempDirectory();
        var db = new ClipDatabase(Path.Combine(dbDir.Path, "clips.db"), NullLogger<ClipDatabase>.Instance);
        var repo = new ClipRepository(db);
        var idx = new LibraryIndexer(repo, NullLogger<LibraryIndexer>.Instance);
        return (idx, repo, output, dbDir);
    }

    private static async Task WritePair(string dir, string stem, string sidecarJson, CancellationToken ct)
    {
        var rewritten = System.Text.RegularExpressions.Regex.Replace(
            sidecarJson, "\"videoFile\":\\s*\"[^\"]+\"", $"\"videoFile\": \"{stem}.mp4\"");
        await File.WriteAllTextAsync(Path.Combine(dir, $"{stem}.json"), rewritten, ct);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{stem}.mp4"), "fake", ct);
    }

    [Fact]
    public async Task V1_mythic_plus_sidecar_backfills_dungeon_name_from_display_name()
    {
        var (idx, repo, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await WritePair(output.Path, "stonevault", V1MythicPlusSidecar, ct);

            var result = await idx.ReconcileAsync(output.Path, ct);

            Assert.Equal(1, result.ClipsAdded);
            var clip = await repo.GetClipByVideoPathAsync(Path.Combine(output.Path, "stonevault.mp4"), ct);
            Assert.NotNull(clip);
            Assert.Equal("The Stonevault", clip!.DungeonName);
            Assert.Null(clip.InstanceName);
            Assert.Null(clip.PlayerName);
        }
    }

    [Fact]
    public async Task Indexer_sets_has_events_when_events_sidecar_exists_on_disk()
    {
        var (idx, repo, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await WritePair(output.Path, "ulgrax", V2RaidSidecar, ct);
            // Plant an events sidecar on disk.
            await File.WriteAllTextAsync(
                Path.Combine(output.Path, "ulgrax.events.json"),
                "{\"version\":1,\"recordingStartedAt\":\"2026-05-09T19:43:01+00:00\"," +
                "\"recordingEndedAt\":\"2026-05-09T19:47:55+00:00\",\"events\":[]}", ct);

            await idx.ReconcileAsync(output.Path, ct);

            var clip = await repo.GetClipByVideoPathAsync(Path.Combine(output.Path, "ulgrax.mp4"), ct);
            Assert.NotNull(clip);
            Assert.True(clip!.HasEvents);
            Assert.EndsWith(".events.json", clip.EventsSidecarPath ?? "");
        }
    }

    [Fact]
    public async Task Indexer_clears_has_events_when_events_sidecar_disappears()
    {
        var (idx, repo, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await WritePair(output.Path, "ulgrax", V2RaidSidecar, ct);
            var eventsPath = Path.Combine(output.Path, "ulgrax.events.json");
            await File.WriteAllTextAsync(eventsPath,
                "{\"version\":1,\"recordingStartedAt\":\"2026-05-09T19:43:01+00:00\"," +
                "\"recordingEndedAt\":\"2026-05-09T19:47:55+00:00\",\"events\":[]}", ct);

            await idx.ReconcileAsync(output.Path, ct);
            File.Delete(eventsPath);
            await idx.ReconcileAsync(output.Path, ct);

            var clip = await repo.GetClipByVideoPathAsync(Path.Combine(output.Path, "ulgrax.mp4"), ct);
            Assert.NotNull(clip);
            Assert.False(clip!.HasEvents);
            Assert.Null(clip.EventsSidecarPath);
        }
    }

    [Fact]
    public async Task Indexer_skips_events_sidecars_themselves_as_clip_input()
    {
        // X.events.json should never be treated as a primary sidecar; only
        // X.json (without .events) drives clip rows. The events file would
        // fail to deserialize and bump the error count if not filtered out.
        var (idx, repo, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await WritePair(output.Path, "ulgrax", V2RaidSidecar, ct);
            await File.WriteAllTextAsync(
                Path.Combine(output.Path, "ulgrax.events.json"),
                "{\"version\":1,\"recordingStartedAt\":\"2026-05-09T19:43:01+00:00\"," +
                "\"recordingEndedAt\":\"2026-05-09T19:47:55+00:00\",\"events\":[]}", ct);

            var result = await idx.ReconcileAsync(output.Path, ct);

            Assert.Equal(1, result.ClipsAdded);
            Assert.Equal(0, result.Errors);
        }
    }

    [Fact]
    public async Task V2_raid_sidecar_passes_player_and_instance_through()
    {
        var (idx, repo, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await WritePair(output.Path, "ulgrax", V2RaidSidecar, ct);

            await idx.ReconcileAsync(output.Path, ct);

            var clip = await repo.GetClipByVideoPathAsync(Path.Combine(output.Path, "ulgrax.mp4"), ct);
            Assert.NotNull(clip);
            Assert.Equal("Frostytanegl", clip!.PlayerName);
            Assert.Equal("Stormscale", clip.PlayerRealm);
            Assert.Equal("Mage", clip.PlayerClass);
            Assert.Equal("Frost", clip.PlayerSpec);
            Assert.Equal("Liberation of Undermine", clip.InstanceName);
            Assert.Null(clip.DungeonName);
        }
    }
}
