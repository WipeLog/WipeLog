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

using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.App.Services;
using Wipelog.Core.Detection;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class ClipArchiverTests
{
    private static ClipRecord MakeClip(TempDirectory dir, string stem,
        bool writeEvents = true, int videoBytes = 1024)
    {
        var video = Path.Combine(dir.Path, stem + ".mp4");
        var sidecar = Path.Combine(dir.Path, stem + ".json");
        var events = Path.Combine(dir.Path, stem + ".events.json");
        File.WriteAllBytes(video, new byte[videoBytes]);
        File.WriteAllText(sidecar, "{\"displayName\":\"" + stem + "\"}");
        if (writeEvents) File.WriteAllText(events, "{\"version\":2}");
        return new ClipRecord(
            Id: 0, VideoPath: video, SidecarPath: sidecar,
            ContentType: ContentType.RaidEncounter, DisplayName: stem,
            Difficulty: "Mythic", EncounterId: 1, KeystoneLevel: null, Success: true,
            StartedAt: DateTimeOffset.UtcNow, EndedAt: DateTimeOffset.UtcNow.AddMinutes(5),
            DurationSeconds: 300, FileSizeBytes: videoBytes, Favorite: false,
            IndexedAt: DateTimeOffset.UtcNow);
    }

    private static ClipArchiver NewArchiver() => new(NullLogger<ClipArchiver>.Instance);

    [Fact]
    public async Task Archives_three_clips_with_three_entries_each()
    {
        using var dir = new TempDirectory();
        var clips = new[]
        {
            MakeClip(dir, "a"),
            MakeClip(dir, "b"),
            MakeClip(dir, "c"),
        };
        var output = Path.Combine(dir.Path, "out.zip");

        var result = await NewArchiver().ArchiveAsync(clips, output, null, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.False(result.WasCancelled);
        Assert.True(File.Exists(output));

        using var zip = ZipFile.OpenRead(output);
        var names = zip.Entries.Select(e => e.FullName).OrderBy(x => x).ToArray();
        Assert.Equal(
            new[]
            {
                "a.events.json", "a.json", "a.mp4",
                "b.events.json", "b.json", "b.mp4",
                "c.events.json", "c.json", "c.mp4",
            },
            names);
    }

    [Fact]
    public async Task Mp4_entry_is_stored_without_compression()
    {
        using var dir = new TempDirectory();
        var clips = new[] { MakeClip(dir, "x", videoBytes: 10_000) };
        var output = Path.Combine(dir.Path, "out.zip");

        await NewArchiver().ArchiveAsync(clips, output, null, TestContext.Current.CancellationToken);

        using var zip = ZipFile.OpenRead(output);
        var mp4 = zip.GetEntry("x.mp4");
        Assert.NotNull(mp4);
        // CompressionLevel.NoCompression → CompressedLength equals Length.
        Assert.Equal(mp4!.Length, mp4.CompressedLength);
    }

    [Fact]
    public async Task Missing_events_sidecar_is_skipped_not_failed()
    {
        using var dir = new TempDirectory();
        var clips = new[] { MakeClip(dir, "x", writeEvents: false) };
        var output = Path.Combine(dir.Path, "out.zip");

        var result = await NewArchiver().ArchiveAsync(clips, output, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);

        using var zip = ZipFile.OpenRead(output);
        Assert.NotNull(zip.GetEntry("x.mp4"));
        Assert.NotNull(zip.GetEntry("x.json"));
        Assert.Null(zip.GetEntry("x.events.json"));
    }

    [Fact]
    public async Task Reports_progress_per_clip()
    {
        using var dir = new TempDirectory();
        var clips = new[] { MakeClip(dir, "a"), MakeClip(dir, "b") };
        var output = Path.Combine(dir.Path, "out.zip");
        var ticks = new List<ArchiveProgress>();
        var progress = new Progress<ArchiveProgress>(p => ticks.Add(p));

        await NewArchiver().ArchiveAsync(clips, output, progress, TestContext.Current.CancellationToken);

        // Allow the Progress<T> callbacks to flush.
        await Task.Yield();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.NotEmpty(ticks);
        Assert.Equal(2, ticks.Last().TotalClips);
        Assert.Equal(2, ticks.Last().CompletedClips);
    }

    [Fact]
    public async Task Cancellation_returns_WasCancelled_and_deletes_partial_file()
    {
        using var dir = new TempDirectory();
        var clips = new[] { MakeClip(dir, "a"), MakeClip(dir, "b"), MakeClip(dir, "c") };
        var output = Path.Combine(dir.Path, "out.zip");
        using var cts = new CancellationTokenSource();
        // Cancel after the first per-clip progress tick fires.
        var progress = new Progress<ArchiveProgress>(p =>
        {
            if (p.CompletedClips >= 1) cts.Cancel();
        });

        var result = await NewArchiver().ArchiveAsync(clips, output, progress, cts.Token);

        Assert.True(result.WasCancelled);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Missing_video_file_counts_as_failed_clip_but_does_not_abort()
    {
        using var dir = new TempDirectory();
        var present = MakeClip(dir, "ok");
        var missing = MakeClip(dir, "gone");
        File.Delete(missing.VideoPath); // simulate a row whose video is gone

        var output = Path.Combine(dir.Path, "out.zip");
        var result = await NewArchiver().ArchiveAsync(new[] { present, missing }, output, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.False(result.WasCancelled);

        using var zip = ZipFile.OpenRead(output);
        Assert.NotNull(zip.GetEntry("ok.mp4"));
    }
}
