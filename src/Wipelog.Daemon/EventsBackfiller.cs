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
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Recording;
using Wipelog.Core.Time;

namespace Wipelog.Daemon;

/// <summary>
/// Best-effort backfill: for each clip in the output directory that lacks a
/// .events.json sidecar, look in the available combat-log files for events
/// whose timestamps fall in [startedAt, endedAt] and replay them through a
/// fresh <see cref="RecordingEventBuffer"/> to produce the missing sidecar.
///
/// "Best-effort": clips whose log window has been rotated away are skipped
/// silently — those events are gone and we don't pretend otherwise.
/// </summary>
internal sealed class EventsBackfiller
{
    private readonly ILogger _logger;
    private readonly IClock _clock;

    public EventsBackfiller(ILogger logger) : this(logger, new SystemClock()) { }

    public EventsBackfiller(ILogger logger, IClock clock)
    {
        _logger = logger;
        _clock = clock;
    }

    public async Task<BackfillResult> RunAsync(
        string outputDirectory,
        string logDirectory,
        string logPattern,
        CancellationToken ct,
        bool force = false)
    {
        if (!Directory.Exists(outputDirectory)) return new BackfillResult(0, 0, 0, 0);
        if (!Directory.Exists(logDirectory))
        {
            _logger.LogWarning("Log directory {Dir} does not exist; nothing to backfill from.", logDirectory);
            return new BackfillResult(0, 0, 0, 0);
        }

        // Collect candidate clips. Without --force we skip clips that already
        // have an events sidecar; with --force we include all clips so each
        // one gets regenerated from the current parser/buffer code.
        var missing = new List<MissingClip>();
        foreach (var sidecarPath in Directory.GetFiles(outputDirectory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            if (sidecarPath.EndsWith(".events.json", StringComparison.Ordinal)) continue;
            var info = TryReadClipWindow(sidecarPath);
            if (info is null) continue;
            var eventsPath = Path.ChangeExtension(info.VideoPath, null) + ".events.json";
            bool eventsExists = File.Exists(eventsPath);
            if (eventsExists && !force) continue;
            missing.Add(new MissingClip(sidecarPath, eventsPath, info.StartedAt, info.EndedAt, eventsExists));
        }
        if (missing.Count == 0) return new BackfillResult(0, 0, 0, 0);

        // Parse all log files in the directory once; group events by minute
        // bucket so per-clip slicing is cheap. For typical recording output
        // (a few sessions of logs) this fits comfortably in memory.
        var parsedEvents = await ParseAllLogsAsync(logDirectory, logPattern, ct).ConfigureAwait(false);

        int written = 0, overwritten = 0, skipped = 0, errors = 0;
        foreach (var clip in missing)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var slice = parsedEvents
                    .Where(e => e.Timestamp >= clip.StartedAt && e.Timestamp <= clip.EndedAt)
                    .ToList();
                if (slice.Count == 0)
                {
                    _logger.LogInformation(
                        "No log data available for {Clip} ({Start} → {End}); skipping.",
                        clip.SidecarPath, clip.StartedAt, clip.EndedAt);
                    skipped++;
                    continue;
                }
                var buffer = new RecordingEventBuffer(clip.StartedAt, TimeSpan.FromSeconds(5));
                foreach (var evt in slice) buffer.Observe(evt);
                var doc = buffer.Build(clip.EndedAt);

                await using var fs = new FileStream(clip.EventsPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                await JsonSerializer.SerializeAsync(fs, doc, TimelineEventsJson.Options, ct).ConfigureAwait(false);
                if (clip.EventsAlreadyExisted) overwritten++;
                written++;
                _logger.LogInformation(
                    "{Verb} {Path} with {EventCount} events.",
                    clip.EventsAlreadyExisted ? "Regenerated" : "Wrote",
                    clip.EventsPath, doc.Events.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backfill failed for {Clip}", clip.SidecarPath);
                errors++;
            }
        }
        return new BackfillResult(written, overwritten, skipped, errors);
    }

    private async Task<List<CombatLogEvent>> ParseAllLogsAsync(
        string logDirectory, string logPattern, CancellationToken ct)
    {
        var parser = new CombatLogParser(_clock);
        var events = new List<CombatLogEvent>();
        foreach (var file in Directory.GetFiles(logDirectory, logPattern))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    var evt = parser.TryParseLine(line);
                    if (evt is not null) events.Add(evt);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read log file {File}; skipping.", file);
            }
        }
        events.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return events;
    }

    /// <summary>
    /// Reads just the timestamp + video file fields from a clip sidecar so we
    /// don't need to round-trip through the full DTO type graph here.
    /// </summary>
    private static ClipWindow? TryReadClipWindow(string sidecarPath)
    {
        try
        {
            using var stream = File.OpenRead(sidecarPath);
            var node = JsonNode.Parse(stream);
            if (node is not JsonObject obj) return null;
            var startedAt = obj["startedAt"]?.GetValue<string>();
            var endedAt = obj["endedAt"]?.GetValue<string>();
            var videoFile = obj["videoFile"]?.GetValue<string>();
            if (startedAt is null || endedAt is null || videoFile is null) return null;
            var dir = Path.GetDirectoryName(sidecarPath) ?? "";
            return new ClipWindow(
                Path.Combine(dir, videoFile),
                DateTimeOffset.Parse(startedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse(endedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }
        catch
        {
            return null;
        }
    }

    private sealed record ClipWindow(string VideoPath, DateTimeOffset StartedAt, DateTimeOffset EndedAt);

    private sealed record MissingClip(
        string SidecarPath, string EventsPath, DateTimeOffset StartedAt, DateTimeOffset EndedAt,
        bool EventsAlreadyExisted);
}

internal sealed record BackfillResult(int Written, int Overwritten, int Skipped, int Errors);
