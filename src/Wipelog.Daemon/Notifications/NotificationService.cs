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

using System.Globalization;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;

namespace Wipelog.Daemon.Notifications;

internal sealed class NotificationService : INotificationService
{
    private const string AppName = "Wipelog";
    private const string Icon = "video-x-generic";

    private readonly INotifySendInvoker _invoker;
    private readonly NotificationOptions _options;

    public NotificationService(INotifySendInvoker invoker, NotificationOptions options)
    {
        _invoker = invoker;
        _options = options;
    }

    public Task NotifyClipSavedAsync(ClipFinalization clip, CancellationToken ct)
    {
        if (!_options.ClipSavedEnabled) return Task.CompletedTask;
        var args = BuildArgs(
            urgency: "normal",
            timeoutMs: 5000,
            title: "Clip saved",
            body: FormatClipBody(clip));
        return _invoker.InvokeAsync(args, ct);
    }

    public Task NotifyErrorAsync(string title, string message, CancellationToken ct)
    {
        if (!_options.ErrorsEnabled) return Task.CompletedTask;
        var args = BuildArgs(
            urgency: "critical",
            timeoutMs: 10000,
            title: title,
            body: message);
        return _invoker.InvokeAsync(args, ct);
    }

    public Task NotifyDiskWarningAsync(long freeGb, long minFreeGb, CancellationToken ct)
    {
        // Disk alerts ride the "errors" channel — they're operational warnings,
        // and `--no-notifications` (AllDisabled) suppresses them with everything else.
        if (!_options.ErrorsEnabled) return Task.CompletedTask;
        var args = BuildArgs(
            urgency: "normal",
            timeoutMs: 8000,
            title: "Low disk space",
            body: string.Format(
                CultureInfo.InvariantCulture,
                "{0} GB free (keeping {1} GB). Old clips will be removed as needed.",
                freeGb, minFreeGb));
        return _invoker.InvokeAsync(args, ct);
    }

    public Task NotifyAutoDeleteAsync(int deletedCount, long bytesFreed, CancellationToken ct)
    {
        if (!_options.ErrorsEnabled) return Task.CompletedTask;
        var clips = deletedCount == 1 ? "clip" : "clips";
        var args = BuildArgs(
            urgency: "normal",
            timeoutMs: 8000,
            title: "Freed disk space",
            body: string.Format(
                CultureInfo.InvariantCulture,
                "Auto-deleted {0} old {1} ({2}) to stay under the storage cap.",
                deletedCount, clips, FormatBytes(bytesFreed)));
        return _invoker.InvokeAsync(args, ct);
    }

    public Task NotifyDiskFullFavoritesOnlyAsync(CancellationToken ct)
    {
        if (!_options.ErrorsEnabled) return Task.CompletedTask;
        var args = BuildArgs(
            urgency: "critical",
            timeoutMs: 15000,
            title: "Disk almost full",
            body: "Free space is below the limit but all remaining clips are favorited. Manual cleanup required.");
        return _invoker.InvokeAsync(args, ct);
    }

    internal static string FormatBytes(long bytes)
    {
        double b = Math.Max(0, bytes);
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int u = 0;
        while (b >= 1024 && u < units.Length - 1) { b /= 1024; u++; }
        return string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", b, units[u]);
    }

    internal static List<string> BuildArgs(string urgency, int timeoutMs, string title, string body) => new()
    {
        "-a", AppName,
        "-i", Icon,
        "-u", urgency,
        "-t", timeoutMs.ToString(CultureInfo.InvariantCulture),
        title,
        body,
    };

    internal static string FormatClipBody(ClipFinalization clip)
    {
        var result = ResultLabel(clip);
        var duration = FormatDuration(clip.DurationSeconds);
        return $"{clip.DisplayName} ({result}) · {duration}";
    }

    private static string ResultLabel(ClipFinalization clip) => clip.Type switch
    {
        ContentType.RaidEncounter => clip.Success ? "Kill" : "Wipe",
        ContentType.MythicPlus => clip.Success ? "Timed" : "Depleted",
        _ => clip.Success ? "Success" : "Failure",
    };

    private static string FormatDuration(int seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}",
                (int)span.TotalHours, span.Minutes, span.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}",
                span.Minutes, span.Seconds);
    }
}
