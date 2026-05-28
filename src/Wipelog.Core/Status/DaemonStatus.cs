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

using System.Text.Json.Serialization;

namespace Wipelog.Core.Status;

public enum DaemonState
{
    Idle,
    Recording,
}

public sealed record RecordingStatus(
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("keystoneLevel")] int? KeystoneLevel);

/// <summary>
/// Whole-drive disk usage for the volume holding the clips, as measured by the
/// daemon and surfaced in the GUI's Storage settings. <see cref="MinFreeBytes"/>
/// is the configured free-space floor the daemon maintains by evicting old
/// clips. <see cref="TotalBytes"/> == 0 means "unknown" (drive couldn't be
/// probed, or a pre-free-space-floor payload).
/// </summary>
public sealed record DiskUsageInfo(
    [property: JsonPropertyName("usedBytes")] long UsedBytes,
    [property: JsonPropertyName("totalBytes")] long TotalBytes,
    [property: JsonPropertyName("freeBytes")] long FreeBytes,
    [property: JsonPropertyName("minFreeBytes")] long MinFreeBytes,
    [property: JsonPropertyName("usagePercent")] double UsagePercent);

public sealed record DaemonStatus(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("heartbeatAt")] DateTimeOffset HeartbeatAt,
    [property: JsonPropertyName("state")] DaemonState State,
    [property: JsonPropertyName("currentRecording")] RecordingStatus? CurrentRecording,
    [property: JsonPropertyName("clipsRecordedThisSession")] long ClipsRecordedThisSession,
    [property: JsonPropertyName("lastError")] string? LastError)
{
    /// <summary>
    /// True when the daemon has observed a <c>COMBAT_LOG_VERSION</c> line
    /// with <c>ADVANCED_LOG_ENABLED=0</c>. Surfaced as a persistent sidebar
    /// warning in the GUI — recordings still happen, but features that
    /// depend on the advanced log block (boss HP%, kill-blow attribution,
    /// raid-comp chip strip) won't have data. Default false so old status
    /// payloads (pre-Tier1-#8) deserialise without false positives.
    /// </summary>
    [JsonPropertyName("advancedLoggingDisabled")]
    public bool AdvancedLoggingDisabled { get; init; }

    /// <summary>
    /// Latest disk-usage measurement, or null when the daemon hasn't measured
    /// yet. Null on old status payloads (pre-Stage-10b) so they deserialise
    /// cleanly; the GUI shows a placeholder in that case.
    /// </summary>
    [JsonPropertyName("diskUsage")]
    public DiskUsageInfo? DiskUsage { get; init; }
}
