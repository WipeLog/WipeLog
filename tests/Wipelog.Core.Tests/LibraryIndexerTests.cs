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

public class LibraryIndexerTests
{
    private const string SampleSidecar = """
        {
          "version": 1,
          "contentType": "RaidEncounter",
          "displayName": "Ulgrax the Devourer",
          "difficulty": "Mythic",
          "encounterId": 2902,
          "success": true,
          "startedAt": "2026-05-09T19:43:01.122+00:00",
          "endedAt": "2026-05-09T19:47:55.890+00:00",
          "durationSeconds": 294,
          "videoFile": "ulgrax.mp4"
        }
        """;

    private const string MythicPlusSidecar = """
        {
          "version": 1,
          "contentType": "MythicPlus",
          "displayName": "Stonevault",
          "keystoneLevel": 12,
          "success": false,
          "startedAt": "2026-05-09T20:15:00+00:00",
          "endedAt": "2026-05-09T20:50:00+00:00",
          "durationSeconds": 2100,
          "videoFile": "stonevault.mp4"
        }
        """;

    private static (LibraryIndexer Indexer, ClipRepository Repo, TempDirectory Output, TempDirectory Db) NewIndexer()
    {
        var output = new TempDirectory();
        var dbDir = new TempDirectory();
        var db = new ClipDatabase(Path.Combine(dbDir.Path, "clips.db"), NullLogger<ClipDatabase>.Instance);
        var repo = new ClipRepository(db);
        var indexer = new LibraryIndexer(repo, NullLogger<LibraryIndexer>.Instance);
        return (indexer, repo, output, dbDir);
    }

    private static async Task WriteClipPair(string dir, string stem, string sidecarJson, CancellationToken ct)
    {
        // Rewrite the videoFile field so the sidecar references *this* stem,
        // not the one literal in the SampleSidecar/MythicPlusSidecar fixture.
        var rewritten = System.Text.RegularExpressions.Regex.Replace(
            sidecarJson,
            "\"videoFile\":\\s*\"[^\"]+\"",
            $"\"videoFile\": \"{stem}.mp4\"");
        await File.WriteAllTextAsync(Path.Combine(dir, $"{stem}.json"), rewritten, ct);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{stem}.mp4"), "fake video bytes", ct);
    }

    [Fact]
    public async Task Empty_directory_returns_zero_counts()
    {
        var (idx, _, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            var result = await idx.ReconcileAsync(output.Path, ct);
            Assert.Equal(new IndexerResult(0, 0, 0), result);
        }
    }

    [Fact]
    public async Task New_sidecar_with_video_is_inserted()
    {
        var (idx, repo, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await WriteClipPair(output.Path, "ulgrax", SampleSidecar, ct);

            var result = await idx.ReconcileAsync(output.Path, ct);

            Assert.Equal(1, result.ClipsAdded);
            Assert.Equal(0, result.ClipsRemoved);
            var clip = await repo.GetClipByVideoPathAsync(Path.Combine(output.Path, "ulgrax.mp4"), ct);
            Assert.NotNull(clip);
            Assert.Equal("Ulgrax the Devourer", clip!.DisplayName);
            Assert.Equal(2902, clip.EncounterId);
        }
    }

    [Fact]
    public async Task Sidecar_without_matching_video_is_an_error()
    {
        var (idx, _, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await File.WriteAllTextAsync(Path.Combine(output.Path, "orphan.json"), SampleSidecar, ct);

            var result = await idx.ReconcileAsync(output.Path, ct);

            Assert.Equal(0, result.ClipsAdded);
            Assert.Equal(1, result.Errors);
        }
    }

    [Fact]
    public async Task Existing_row_with_missing_video_is_removed()
    {
        var (idx, repo, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await WriteClipPair(output.Path, "gone", SampleSidecar, ct);
            await idx.ReconcileAsync(output.Path, ct);
            File.Delete(Path.Combine(output.Path, "gone.mp4"));

            var result = await idx.ReconcileAsync(output.Path, ct);

            Assert.Equal(1, result.ClipsRemoved);
            Assert.Empty(await repo.QueryClipsAsync(new ClipQuery(), ct));
        }
    }

    [Fact]
    public async Task Reindexing_is_idempotent()
    {
        var (idx, _, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await WriteClipPair(output.Path, "ulgrax", SampleSidecar, ct);
            await WriteClipPair(output.Path, "stonevault", MythicPlusSidecar, ct);

            var first = await idx.ReconcileAsync(output.Path, ct);
            var second = await idx.ReconcileAsync(output.Path, ct);

            Assert.Equal(2, first.ClipsAdded);
            Assert.Equal(0, second.ClipsAdded);
            Assert.Equal(0, second.ClipsRemoved);
        }
    }

    [Fact]
    public async Task File_size_is_read_from_disk()
    {
        var (idx, repo, output, db) = NewIndexer();
        using (output) using (db)
        {
            var ct = TestContext.Current.CancellationToken;
            await WriteClipPair(output.Path, "x", SampleSidecar, ct);
            var expected = new FileInfo(Path.Combine(output.Path, "x.mp4")).Length;

            await idx.ReconcileAsync(output.Path, ct);

            var clip = await repo.GetClipByVideoPathAsync(Path.Combine(output.Path, "x.mp4"), ct);
            Assert.Equal(expected, clip!.FileSizeBytes);
        }
    }
}
