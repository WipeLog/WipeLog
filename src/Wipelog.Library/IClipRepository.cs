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

public interface IClipRepository
{
    Task<long> InsertOrUpdateClipAsync(ClipRecord clip, CancellationToken ct);
    Task<ClipRecord?> GetClipAsync(long id, CancellationToken ct);
    Task<ClipRecord?> GetClipByVideoPathAsync(string videoPath, CancellationToken ct);
    Task<IReadOnlyList<ClipRecord>> QueryClipsAsync(ClipQuery query, CancellationToken ct);

    /// <summary>
    /// Up to <paramref name="max"/> non-favorited clips, oldest first (by
    /// <c>started_at</c>). Used by the daemon's disk-cleanup path to pick
    /// eviction candidates — favorited clips are excluded and never returned.
    /// </summary>
    Task<IReadOnlyList<ClipRecord>> GetOldestNonFavoritedClipsAsync(int max, CancellationToken ct);
    Task<IReadOnlyDictionary<ContentType, int>> GetCountsByContentTypeAsync(CancellationToken ct);
    Task SetFavoriteAsync(long clipId, bool favorite, CancellationToken ct);
    Task SetNotesAsync(long clipId, string? notes, CancellationToken ct);
    Task<IReadOnlyList<string>> GetTagsForClipAsync(long clipId, CancellationToken ct);
    Task SetTagsForClipAsync(long clipId, IReadOnlyList<string> tagNames, CancellationToken ct);
    Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct);
    Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> GetTagsForClipsAsync(IReadOnlyList<long> clipIds, CancellationToken ct);
    Task DeleteClipAsync(long clipId, CancellationToken ct);
    Task<BulkDeleteResult> DeleteClipsAsync(IReadOnlyList<long> ids, CancellationToken ct);
    Task UpdateClipMetadataAsync(long clipId, ClipMetadataPatch patch, CancellationToken ct);
}

/// <summary>
/// Outcome of a bulk delete: <see cref="Succeeded"/> is the number of DB rows
/// removed; <see cref="FailedFiles"/> is the count of file-delete attempts
/// that threw (per-clip there are up to three files — video + two sidecars).
/// </summary>
public sealed record BulkDeleteResult(int Succeeded, int FailedFiles);
