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
using Wipelog.Core.Recording;
using Wipelog.Daemon.Notifications;
using Xunit;

namespace Wipelog.Core.Tests;

public class NotificationServiceTests
{
    private sealed class CapturingInvoker : INotifySendInvoker
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public Task InvokeAsync(IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls.Add(arguments.ToList());
            return Task.CompletedTask;
        }
    }

    private static ClipFinalization MakeRaidClip(bool success, int durationSeconds = 180) => new(
        VideoPath: "/tmp/clip.mp4",
        SidecarPath: "/tmp/clip.json",
        EventsSidecarPath: null,
        Type: ContentType.RaidEncounter,
        DisplayName: "Ulgrax the Devourer",
        DifficultyId: 16,
        EncounterId: 2902,
        KeystoneLevel: null,
        Success: success,
        StartedAt: new DateTimeOffset(2026, 5, 11, 19, 0, 0, TimeSpan.Zero),
        EndedAt: new DateTimeOffset(2026, 5, 11, 19, 0, 0, TimeSpan.Zero).AddSeconds(durationSeconds),
        DurationSeconds: durationSeconds,
        FileSizeBytes: 1024,
        PlayerName: null, PlayerRealm: null, PlayerRegion: null,
        PlayerClass: null, PlayerSpec: null,
        InstanceName: null, DungeonName: null);

    [Fact]
    public async Task NotifyClipSavedAsync_builds_expected_argv()
    {
        var invoker = new CapturingInvoker();
        var svc = new NotificationService(invoker, NotificationOptions.AllEnabled);

        await svc.NotifyClipSavedAsync(MakeRaidClip(success: true, durationSeconds: 294), CancellationToken.None);

        var args = Assert.Single(invoker.Calls);
        Assert.Contains("-a", args);
        Assert.Contains("Wipelog", args);
        Assert.Contains("-i", args);
        Assert.Contains("video-x-generic", args);
        Assert.Contains("-u", args);
        Assert.Contains("normal", args);
        Assert.Contains("-t", args);
        Assert.Contains("5000", args);
        Assert.Contains("Clip saved", args);
        Assert.Contains("Ulgrax the Devourer (Kill) · 4:54", args);
    }

    [Fact]
    public async Task NotifyClipSavedAsync_labels_mplus_depleted()
    {
        var invoker = new CapturingInvoker();
        var svc = new NotificationService(invoker, NotificationOptions.AllEnabled);

        var clip = MakeRaidClip(success: false) with
        {
            Type = ContentType.MythicPlus,
            DisplayName = "Ara-Kara, City of Echoes",
        };
        await svc.NotifyClipSavedAsync(clip, CancellationToken.None);

        var args = Assert.Single(invoker.Calls);
        Assert.Contains("Ara-Kara, City of Echoes (Depleted) · 3:00", args);
    }

    [Fact]
    public async Task NotifyErrorAsync_uses_critical_urgency_and_longer_timeout()
    {
        var invoker = new CapturingInvoker();
        var svc = new NotificationService(invoker, NotificationOptions.AllEnabled);

        await svc.NotifyErrorAsync("Wipelog error", "Recorder crashed", CancellationToken.None);

        var args = Assert.Single(invoker.Calls);
        Assert.Contains("critical", args);
        Assert.Contains("10000", args);
        Assert.Contains("Wipelog error", args);
        Assert.Contains("Recorder crashed", args);
    }

    [Fact]
    public async Task Notifications_are_skipped_when_disabled()
    {
        var invoker = new CapturingInvoker();
        var svc = new NotificationService(invoker, NotificationOptions.AllDisabled);

        await svc.NotifyClipSavedAsync(MakeRaidClip(success: true), CancellationToken.None);
        await svc.NotifyErrorAsync("title", "msg", CancellationToken.None);

        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public async Task Selective_disable_clip_saved_only()
    {
        var invoker = new CapturingInvoker();
        var svc = new NotificationService(invoker, new NotificationOptions(ClipSavedEnabled: false, ErrorsEnabled: true));

        await svc.NotifyClipSavedAsync(MakeRaidClip(success: true), CancellationToken.None);
        await svc.NotifyErrorAsync("title", "msg", CancellationToken.None);

        Assert.Single(invoker.Calls);
        Assert.Contains("title", invoker.Calls[0]);
    }
}
