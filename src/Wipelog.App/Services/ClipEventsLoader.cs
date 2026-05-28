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

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Recording;

namespace Wipelog.App.Services;

/// <summary>
/// Loads the <c>.events.json</c> sidecar that sits next to a clip's video
/// file. Returns null (not throws) when the sidecar is missing or unreadable —
/// the timeline view falls back to an "empty state" rather than surfacing
/// the error, because for older clips the sidecar legitimately doesn't exist.
/// </summary>
public interface IClipEventsLoader
{
    Task<TimelineEventDocument?> LoadAsync(string videoPath, CancellationToken ct);
}

public sealed class ClipEventsLoader : IClipEventsLoader
{
    private readonly ILogger<ClipEventsLoader> _logger;

    public ClipEventsLoader(ILogger<ClipEventsLoader> logger)
    {
        _logger = logger;
    }

    public static string EventsPathFor(string videoPath) =>
        Path.ChangeExtension(videoPath, null) + ".events.json";

    public async Task<TimelineEventDocument?> LoadAsync(string videoPath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(videoPath)) return null;
        var eventsPath = EventsPathFor(videoPath);
        if (!File.Exists(eventsPath)) return null;
        try
        {
            await using var stream = File.OpenRead(eventsPath);
            return await JsonSerializer.DeserializeAsync<TimelineEventDocument>(
                stream, TimelineEventsJson.Options, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load events sidecar at {Path}", eventsPath);
            return null;
        }
    }
}
