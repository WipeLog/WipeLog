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
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Data;
using Wipelog.Core.Detection;

namespace Wipelog.Library;

public interface ILibraryIndexer
{
    Task<IndexerResult> ReconcileAsync(string outputDirectory, CancellationToken ct);
}

public sealed record IndexerResult(int ClipsAdded, int ClipsRemoved, int Errors);

public sealed class LibraryIndexer : ILibraryIndexer
{
    private const int BatchSize = 500;

    private readonly IClipRepository _repo;
    private readonly ILogger<LibraryIndexer> _logger;

    public LibraryIndexer(IClipRepository repo, ILogger<LibraryIndexer> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<IndexerResult> ReconcileAsync(string outputDirectory, CancellationToken ct)
    {
        int added = 0, removed = 0, errors = 0;

        if (Directory.Exists(outputDirectory))
        {
            // Skip the events sidecars — they're not full clip metadata, just
            // timeline data. Only the regular sidecars (X.json, NOT
            // X.events.json) drive clip rows.
            var sidecars = Directory.GetFiles(outputDirectory, "*.json")
                .Where(p => !p.EndsWith(".events.json", StringComparison.Ordinal));
            foreach (var sidecarPath in sidecars)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var dto = await ReadSidecarAsync(sidecarPath, ct).ConfigureAwait(false);
                    if (dto is null)
                    {
                        errors++;
                        continue;
                    }
                    var videoPath = Path.Combine(outputDirectory, dto.VideoFile);
                    if (!File.Exists(videoPath))
                    {
                        // Sidecar without a video — leave on disk, count as error.
                        _logger.LogWarning("Sidecar references missing video: {Sidecar}", sidecarPath);
                        errors++;
                        continue;
                    }
                    // Always upsert: existing rows' identity fields are
                    // protected by COALESCE in the SQL, so re-indexing is
                    // safe and lets backfilled dungeon/instance names fill
                    // in on rows that were indexed before commit 2.
                    var existing = await _repo.GetClipByVideoPathAsync(videoPath, ct).ConfigureAwait(false);
                    var record = ToRecord(dto, videoPath, sidecarPath);
                    await _repo.InsertOrUpdateClipAsync(record, ct).ConfigureAwait(false);
                    if (existing is null) added++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Indexer failed on {Path}", sidecarPath);
                    errors++;
                }
            }
        }

        // Drop rows whose video files no longer exist.
        int offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await _repo.QueryClipsAsync(
                new ClipQuery(Limit: BatchSize, Offset: offset), ct).ConfigureAwait(false);
            if (batch.Count == 0) break;
            foreach (var clip in batch)
            {
                if (!File.Exists(clip.VideoPath))
                {
                    await _repo.DeleteClipAsync(clip.Id, ct).ConfigureAwait(false);
                    removed++;
                }
            }
            if (batch.Count < BatchSize) break;
            offset += batch.Count;
        }

        return new IndexerResult(added, removed, errors);
    }

    private static async Task<SidecarDto?> ReadSidecarAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            stream, IndexerJsonContext.Default.SidecarDto, ct).ConfigureAwait(false);
    }

    private static ClipRecord ToRecord(SidecarDto dto, string videoPath, string sidecarPath)
    {
        var size = new FileInfo(videoPath).Length;
        var contentType = Enum.Parse<ContentType>(dto.ContentType);

        // Backfill instance/dungeon names. v2 sidecars supply them directly;
        // v1 sidecars don't, so derive what we can: M+ → dungeon = displayName;
        // raid → look up instance by encounterId via RaidInstances if possible
        // (best-effort: encounter IDs aren't instance IDs so this stays null
        // unless the sidecar already has it).
        string? instanceName = dto.InstanceName;
        string? dungeonName = dto.DungeonName;
        if (instanceName is null && dungeonName is null)
        {
            if (contentType == ContentType.MythicPlus)
            {
                dungeonName = dto.DisplayName;
            }
        }

        // The events sidecar lives next to the regular sidecar with a
        // .events.json suffix on the video stem. Reconcile the has_events
        // flag from filesystem reality so a hand-deleted file flips back
        // to false on next indexer run, and a backfilled file flips to true.
        var eventsPath = Path.ChangeExtension(videoPath, null) + ".events.json";
        bool hasEvents = File.Exists(eventsPath);

        return new ClipRecord(
            Id: 0,
            VideoPath: videoPath,
            SidecarPath: sidecarPath,
            ContentType: contentType,
            DisplayName: dto.DisplayName,
            Difficulty: dto.Difficulty,
            EncounterId: dto.EncounterId,
            KeystoneLevel: dto.KeystoneLevel,
            Success: dto.Success,
            StartedAt: dto.StartedAt,
            EndedAt: dto.EndedAt,
            DurationSeconds: dto.DurationSeconds,
            FileSizeBytes: size,
            Favorite: false,
            IndexedAt: DateTimeOffset.UtcNow,
            PlayerName: dto.PlayerName,
            PlayerRealm: dto.PlayerRealm,
            PlayerRegion: dto.PlayerRegion,
            PlayerClass: dto.PlayerClass,
            PlayerSpec: dto.PlayerSpec,
            InstanceName: instanceName,
            DungeonName: dungeonName,
            EventsSidecarPath: hasEvents ? eventsPath : null,
            HasEvents: hasEvents,
            ArenaBracket: dto.ArenaBracket,
            // Notes aren't on the sidecar (GUI-only field); COALESCE in the
            // upsert preserves any existing value on re-index.
            Notes: null,
            BossHpPercent: dto.BossHpPercent,
            KeyUpgradeLevel: dto.KeyUpgradeLevel);
    }
}

internal sealed record SidecarDto(
    int Version,
    string ContentType,
    string DisplayName,
    string? Difficulty,
    int? EncounterId,
    int? KeystoneLevel,
    bool Success,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationSeconds,
    string VideoFile,
    string? PlayerName = null,
    string? PlayerRealm = null,
    string? PlayerRegion = null,
    string? PlayerClass = null,
    string? PlayerSpec = null,
    string? InstanceName = null,
    string? DungeonName = null,
    string? ArenaBracket = null,
    double? BossHpPercent = null,
    int? KeyUpgradeLevel = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SidecarDto))]
internal partial class IndexerJsonContext : JsonSerializerContext
{
}
