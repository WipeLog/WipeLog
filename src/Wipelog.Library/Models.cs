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

namespace Wipelog.Library;

public sealed record ClipRecord(
    long Id,
    string VideoPath,
    string SidecarPath,
    ContentType ContentType,
    string DisplayName,
    string? Difficulty,
    int? EncounterId,
    int? KeystoneLevel,
    bool Success,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationSeconds,
    long FileSizeBytes,
    bool Favorite,
    DateTimeOffset IndexedAt,
    string? PlayerName = null,
    string? PlayerRealm = null,
    string? PlayerRegion = null,
    string? PlayerClass = null,
    string? PlayerSpec = null,
    string? InstanceName = null,
    string? DungeonName = null,
    string? EventsSidecarPath = null,
    bool HasEvents = false,
    string? ArenaBracket = null,
    string? Notes = null,
    double? BossHpPercent = null,
    int? KeyUpgradeLevel = null);

/// <summary>
/// Field-level updates applied by <see cref="IClipRepository.UpdateClipMetadataAsync"/>.
/// Nullable fields mean "leave the existing value untouched". Explicit clearing
/// is not supported in v1 — extend with sentinel-typed fields if needed later.
/// </summary>
public sealed record ClipMetadataPatch(
    string? PlayerName = null,
    string? PlayerRealm = null,
    string? PlayerRegion = null,
    string? PlayerClass = null,
    string? PlayerSpec = null,
    bool? Success = null);

public sealed record ClipQuery(
    IReadOnlyList<ContentType>? ContentTypes = null,
    bool? Success = null,
    bool FavoritesOnly = false,
    string? DisplayNameContains = null,
    int? MinKeystoneLevel = null,
    string? Difficulty = null,
    DateTimeOffset? Since = null,
    DateTimeOffset? Before = null,
    IReadOnlyList<string>? AnyOfTags = null,
    string? PlayerName = null,
    string? PlayerRegion = null,
    string? PlayerClass = null,
    string? PlayerSpec = null,
    string? DungeonName = null,
    string? InstanceName = null,
    int Limit = 200,
    int Offset = 0);
