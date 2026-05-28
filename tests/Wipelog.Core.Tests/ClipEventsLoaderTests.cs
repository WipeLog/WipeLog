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
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class ClipEventsLoaderTests
{
    [Fact]
    public async Task LoadAsync_returns_null_when_sidecar_missing()
    {
        using var tmp = new TempDirectory();
        var videoPath = Path.Combine(tmp.Path, "clip.mp4");
        File.WriteAllText(videoPath, "fake");

        var loader = new ClipEventsLoader(NullLogger<ClipEventsLoader>.Instance);
        var doc = await loader.LoadAsync(videoPath, TestContext.Current.CancellationToken);
        Assert.Null(doc);
    }

    [Fact]
    public async Task LoadAsync_returns_document_for_valid_sidecar()
    {
        using var tmp = new TempDirectory();
        var videoPath = Path.Combine(tmp.Path, "clip.mp4");
        File.WriteAllText(videoPath, "fake");

        var start = new DateTimeOffset(2026, 5, 10, 19, 0, 0, TimeSpan.Zero);
        var doc = new TimelineEventDocument(
            Version: 1,
            RecordingStartedAt: start,
            RecordingEndedAt: start.AddMinutes(3),
            Events:
            [
                new PlayerDeathEvent(60_000, "Player-1", "Alice-EU", "Boss", "Cleave", 12345),
                new BossStartEvent(0, 2902, "Chimaerus"),
            ]);
        var eventsPath = ClipEventsLoader.EventsPathFor(videoPath);
        await using (var fs = File.Create(eventsPath))
        {
            await System.Text.Json.JsonSerializer.SerializeAsync(
                fs, doc, TimelineEventsJson.Options, TestContext.Current.CancellationToken);
        }

        var loader = new ClipEventsLoader(NullLogger<ClipEventsLoader>.Instance);
        var loaded = await loader.LoadAsync(videoPath, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Version);
        Assert.Equal(start, loaded.RecordingStartedAt);
        Assert.Equal(2, loaded.Events.Count);
        Assert.Contains(loaded.Events, e => e is PlayerDeathEvent { PlayerName: "Alice-EU" });
        Assert.Contains(loaded.Events, e => e is BossStartEvent { EncounterName: "Chimaerus" });
    }

    [Fact]
    public async Task LoadAsync_returns_null_for_malformed_json_without_throwing()
    {
        using var tmp = new TempDirectory();
        var videoPath = Path.Combine(tmp.Path, "clip.mp4");
        File.WriteAllText(videoPath, "fake");
        File.WriteAllText(ClipEventsLoader.EventsPathFor(videoPath), "{ this is not valid json");

        var loader = new ClipEventsLoader(NullLogger<ClipEventsLoader>.Instance);
        var doc = await loader.LoadAsync(videoPath, TestContext.Current.CancellationToken);
        Assert.Null(doc);
    }

    [Fact]
    public void EventsPathFor_strips_video_extension_and_adds_events_json()
    {
        Assert.Equal("/tmp/foo.events.json", ClipEventsLoader.EventsPathFor("/tmp/foo.mp4"));
        Assert.Equal("/tmp/foo.bar.events.json", ClipEventsLoader.EventsPathFor("/tmp/foo.bar.mp4"));
    }

    [Fact]
    public async Task LoadAsync_returns_null_for_empty_path()
    {
        var loader = new ClipEventsLoader(NullLogger<ClipEventsLoader>.Instance);
        var doc = await loader.LoadAsync("", TestContext.Current.CancellationToken);
        Assert.Null(doc);
    }
}
