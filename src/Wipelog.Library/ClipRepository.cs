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
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Wipelog.Core.Detection;

namespace Wipelog.Library;

public sealed class ClipRepository : IClipRepository
{
    private readonly ClipDatabase _db;

    public ClipRepository(ClipDatabase db)
    {
        _db = db;
    }

    private static readonly string ClipColumns =
        "id, video_path, sidecar_path, content_type, display_name, difficulty, " +
        "encounter_id, keystone_level, success, started_at, ended_at, " +
        "duration_seconds, file_size_bytes, favorite, indexed_at, " +
        "player_name, player_realm, player_region, player_class, player_spec, " +
        "instance_name, dungeon_name, events_sidecar_path, has_events, arena_bracket, notes, " +
        "boss_hp_percent, key_upgrade_level";

    public async Task<long> InsertOrUpdateClipAsync(ClipRecord clip, CancellationToken ct)
    {
        await using var conn = _db.OpenConnection();
        const string sql = """
            INSERT INTO clips (
                video_path, sidecar_path, content_type, display_name, difficulty,
                encounter_id, keystone_level, success, started_at, ended_at,
                duration_seconds, file_size_bytes, favorite, indexed_at,
                player_name, player_realm, player_region, player_class, player_spec,
                instance_name, dungeon_name, events_sidecar_path, has_events,
                arena_bracket, notes, boss_hp_percent, key_upgrade_level
            )
            VALUES (
                @VideoPath, @SidecarPath, @ContentType, @DisplayName, @Difficulty,
                @EncounterId, @KeystoneLevel, @Success, @StartedAt, @EndedAt,
                @DurationSeconds, @FileSizeBytes, @Favorite, @IndexedAt,
                @PlayerName, @PlayerRealm, @PlayerRegion, @PlayerClass, @PlayerSpec,
                @InstanceName, @DungeonName, @EventsSidecarPath, @HasEvents,
                @ArenaBracket, @Notes, @BossHpPercent, @KeyUpgradeLevel
            )
            ON CONFLICT(video_path) DO UPDATE SET
                sidecar_path        = excluded.sidecar_path,
                content_type        = excluded.content_type,
                display_name        = excluded.display_name,
                difficulty          = excluded.difficulty,
                encounter_id        = excluded.encounter_id,
                keystone_level      = excluded.keystone_level,
                success             = excluded.success,
                started_at          = excluded.started_at,
                ended_at            = excluded.ended_at,
                duration_seconds    = excluded.duration_seconds,
                file_size_bytes     = excluded.file_size_bytes,
                indexed_at          = excluded.indexed_at,
                player_name         = COALESCE(excluded.player_name, clips.player_name),
                player_realm        = COALESCE(excluded.player_realm, clips.player_realm),
                player_region       = COALESCE(excluded.player_region, clips.player_region),
                player_class        = COALESCE(excluded.player_class, clips.player_class),
                player_spec         = COALESCE(excluded.player_spec, clips.player_spec),
                instance_name       = COALESCE(excluded.instance_name, clips.instance_name),
                dungeon_name        = COALESCE(excluded.dungeon_name, clips.dungeon_name),
                events_sidecar_path = excluded.events_sidecar_path,
                has_events          = excluded.has_events,
                arena_bracket       = COALESCE(excluded.arena_bracket, clips.arena_bracket),
                notes               = COALESCE(excluded.notes, clips.notes),
                boss_hp_percent     = COALESCE(excluded.boss_hp_percent, clips.boss_hp_percent),
                key_upgrade_level   = COALESCE(excluded.key_upgrade_level, clips.key_upgrade_level)
            RETURNING id;
            """;
        var parameters = new
        {
            clip.VideoPath,
            clip.SidecarPath,
            ContentType = clip.ContentType.ToString(),
            clip.DisplayName,
            clip.Difficulty,
            clip.EncounterId,
            clip.KeystoneLevel,
            Success = clip.Success ? 1 : 0,
            StartedAt = FormatDate(clip.StartedAt),
            EndedAt = FormatDate(clip.EndedAt),
            clip.DurationSeconds,
            clip.FileSizeBytes,
            Favorite = clip.Favorite ? 1 : 0,
            IndexedAt = FormatDate(clip.IndexedAt),
            clip.PlayerName,
            clip.PlayerRealm,
            clip.PlayerRegion,
            clip.PlayerClass,
            clip.PlayerSpec,
            clip.InstanceName,
            clip.DungeonName,
            clip.EventsSidecarPath,
            HasEvents = clip.HasEvents ? 1 : 0,
            clip.ArenaBracket,
            clip.Notes,
            clip.BossHpPercent,
            clip.KeyUpgradeLevel,
        };
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    public async Task<ClipRecord?> GetClipAsync(long id, CancellationToken ct)
    {
        await using var conn = _db.OpenConnection();
        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition($"SELECT {ClipColumns} FROM clips WHERE id = @Id",
                new { Id = id }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<ClipRecord?> GetClipByVideoPathAsync(string videoPath, CancellationToken ct)
    {
        await using var conn = _db.OpenConnection();
        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition($"SELECT {ClipColumns} FROM clips WHERE video_path = @Path",
                new { Path = videoPath }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<ClipRecord>> QueryClipsAsync(ClipQuery query, CancellationToken ct)
    {
        var sb = new StringBuilder($"SELECT {ClipColumns} FROM clips WHERE 1=1");
        var p = new DynamicParameters();

        if (query.ContentTypes is { Count: > 0 })
        {
            var names = query.ContentTypes.Select(c => c.ToString()).ToArray();
            sb.Append(" AND content_type IN @ContentTypes");
            p.Add("ContentTypes", names);
        }
        if (query.Success is { } s)
        {
            sb.Append(" AND success = @Success");
            p.Add("Success", s ? 1 : 0);
        }
        if (query.FavoritesOnly)
        {
            sb.Append(" AND favorite = 1");
        }
        if (!string.IsNullOrWhiteSpace(query.DisplayNameContains))
        {
            sb.Append(" AND display_name LIKE @NameLike");
            p.Add("NameLike", "%" + EscapeLike(query.DisplayNameContains) + "%");
        }
        if (query.MinKeystoneLevel is { } k)
        {
            sb.Append(" AND keystone_level >= @MinK");
            p.Add("MinK", k);
        }
        if (!string.IsNullOrWhiteSpace(query.Difficulty))
        {
            sb.Append(" AND difficulty = @Difficulty");
            p.Add("Difficulty", query.Difficulty);
        }
        if (query.Since is { } since)
        {
            sb.Append(" AND started_at >= @Since");
            p.Add("Since", FormatDate(since));
        }
        if (query.Before is { } before)
        {
            // Half-open interval — callers wanting an inclusive end-of-day
            // pass `endOfDay + 1`. Mirrors the Since lower-bound shape so the
            // two clauses compose cleanly.
            sb.Append(" AND started_at < @Before");
            p.Add("Before", FormatDate(before));
        }
        if (!string.IsNullOrWhiteSpace(query.PlayerName))
        {
            sb.Append(" AND player_name = @PlayerName");
            p.Add("PlayerName", query.PlayerName);
        }
        if (!string.IsNullOrWhiteSpace(query.PlayerRegion))
        {
            sb.Append(" AND player_region = @PlayerRegion");
            p.Add("PlayerRegion", query.PlayerRegion);
        }
        if (!string.IsNullOrWhiteSpace(query.PlayerClass))
        {
            sb.Append(" AND player_class = @PlayerClass");
            p.Add("PlayerClass", query.PlayerClass);
        }
        if (!string.IsNullOrWhiteSpace(query.PlayerSpec))
        {
            sb.Append(" AND player_spec = @PlayerSpec");
            p.Add("PlayerSpec", query.PlayerSpec);
        }
        if (!string.IsNullOrWhiteSpace(query.DungeonName))
        {
            sb.Append(" AND dungeon_name = @DungeonName");
            p.Add("DungeonName", query.DungeonName);
        }
        if (!string.IsNullOrWhiteSpace(query.InstanceName))
        {
            sb.Append(" AND instance_name = @InstanceName");
            p.Add("InstanceName", query.InstanceName);
        }
        if (query.AnyOfTags is { Count: > 0 })
        {
            var names = query.AnyOfTags
                .Select(t => TagNormalization.TryNormalize(t, out var n) ? n : null)
                .Where(n => n is not null)
                .ToArray();
            if (names.Length > 0)
            {
                sb.Append(" AND id IN (SELECT clip_id FROM clip_tags ct " +
                          "JOIN tags t ON t.id = ct.tag_id WHERE t.name IN @TagNames)");
                p.Add("TagNames", names);
            }
        }
        sb.Append(" ORDER BY started_at DESC LIMIT @Limit OFFSET @Offset");
        p.Add("Limit", query.Limit);
        p.Add("Offset", query.Offset);

        await using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<dynamic>(
            new CommandDefinition(sb.ToString(), p, cancellationToken: ct));
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ClipRecord>> GetOldestNonFavoritedClipsAsync(int max, CancellationToken ct)
    {
        if (max <= 0) return Array.Empty<ClipRecord>();
        await using var conn = _db.OpenConnection();
        var sql = $"SELECT {ClipColumns} FROM clips WHERE favorite = 0 " +
                  "ORDER BY started_at ASC LIMIT @Max";
        var rows = await conn.QueryAsync<dynamic>(
            new CommandDefinition(sql, new { Max = max }, cancellationToken: ct));
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyDictionary<ContentType, int>> GetCountsByContentTypeAsync(CancellationToken ct)
    {
        await using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<(string ContentType, long Count)>(new CommandDefinition(
            "SELECT content_type AS ContentType, COUNT(*) AS Count FROM clips GROUP BY content_type",
            cancellationToken: ct));

        var counts = new Dictionary<ContentType, int>();
        foreach (var (typeName, count) in rows)
        {
            if (Enum.TryParse<ContentType>(typeName, out var parsed))
            {
                counts[parsed] = (int)count;
            }
        }
        return counts;
    }

    public async Task SetFavoriteAsync(long clipId, bool favorite, CancellationToken ct)
    {
        await using var conn = _db.OpenConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE clips SET favorite = @Fav WHERE id = @Id",
            new { Fav = favorite ? 1 : 0, Id = clipId },
            cancellationToken: ct));
    }

    public async Task SetNotesAsync(long clipId, string? notes, CancellationToken ct)
    {
        await using var conn = _db.OpenConnection();
        // Store empty strings as null so "user cleared the notes" and "user
        // never typed any notes" round-trip identically through the column.
        var normalized = string.IsNullOrWhiteSpace(notes) ? null : notes;
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE clips SET notes = @Notes WHERE id = @Id",
            new { Notes = normalized, Id = clipId },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<string>> GetTagsForClipAsync(long clipId, CancellationToken ct)
    {
        await using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT t.name FROM tags t " +
            "JOIN clip_tags ct ON ct.tag_id = t.id " +
            "WHERE ct.clip_id = @Id ORDER BY t.name",
            new { Id = clipId },
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SetTagsForClipAsync(long clipId, IReadOnlyList<string> tagNames, CancellationToken ct)
    {
        var normalized = tagNames
            .Select(t => TagNormalization.TryNormalize(t, out var n) ? n : null)
            .Where(n => n is not null)
            .Distinct()
            .ToList();

        await using var conn = _db.OpenConnection();
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM clip_tags WHERE clip_id = @Id",
            new { Id = clipId }, transaction: tx, cancellationToken: ct));

        foreach (var name in normalized)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT OR IGNORE INTO tags (name) VALUES (@Name)",
                new { Name = name }, transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT OR IGNORE INTO clip_tags (clip_id, tag_id) " +
                "SELECT @Id, id FROM tags WHERE name = @Name",
                new { Id = clipId, Name = name }, transaction: tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct)
    {
        await using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM tags ORDER BY name",
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> GetTagsForClipsAsync(
        IReadOnlyList<long> clipIds, CancellationToken ct)
    {
        if (clipIds.Count == 0)
        {
            return new Dictionary<long, IReadOnlyList<string>>();
        }
        await using var conn = _db.OpenConnection();
        var rows = await conn.QueryAsync<(long ClipId, string Name)>(new CommandDefinition(
            "SELECT ct.clip_id AS ClipId, t.name AS Name " +
            "FROM clip_tags ct JOIN tags t ON t.id = ct.tag_id " +
            "WHERE ct.clip_id IN @Ids ORDER BY ct.clip_id, t.name",
            new { Ids = clipIds },
            cancellationToken: ct));

        var dict = new Dictionary<long, List<string>>();
        foreach (var r in rows)
        {
            if (!dict.TryGetValue(r.ClipId, out var list))
            {
                list = new List<string>();
                dict[r.ClipId] = list;
            }
            list.Add(r.Name);
        }
        return dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    public async Task UpdateClipMetadataAsync(long clipId, ClipMetadataPatch patch, CancellationToken ct)
    {
        // Sidecar first, DB second. If the sidecar write fails we abort
        // before touching the DB; if the DB write fails the next reconcile
        // pass replays the sidecar back into the DB. Doing them the other
        // way would let a DB-only success silently revert on next reconcile.
        string? sidecarPath = null;
        await using (var conn = _db.OpenConnection())
        {
            sidecarPath = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT sidecar_path FROM clips WHERE id = @Id",
                new { Id = clipId }, cancellationToken: ct));
        }
        if (sidecarPath is not null)
        {
            await UpdateSidecarAsync(sidecarPath, patch, ct).ConfigureAwait(false);
        }

        var sets = new List<string>();
        var p = new DynamicParameters();
        p.Add("Id", clipId);
        if (patch.PlayerName is not null)
        {
            sets.Add("player_name = @PlayerName");
            p.Add("PlayerName", patch.PlayerName);
        }
        if (patch.PlayerRealm is not null)
        {
            sets.Add("player_realm = @PlayerRealm");
            p.Add("PlayerRealm", patch.PlayerRealm);
        }
        if (patch.PlayerRegion is not null)
        {
            sets.Add("player_region = @PlayerRegion");
            p.Add("PlayerRegion", patch.PlayerRegion);
        }
        if (patch.PlayerClass is not null)
        {
            sets.Add("player_class = @PlayerClass");
            p.Add("PlayerClass", patch.PlayerClass);
        }
        if (patch.PlayerSpec is not null)
        {
            sets.Add("player_spec = @PlayerSpec");
            p.Add("PlayerSpec", patch.PlayerSpec);
        }
        if (patch.Success is { } success)
        {
            sets.Add("success = @Success");
            p.Add("Success", success ? 1 : 0);
        }
        if (sets.Count == 0) return;

        var sql = $"UPDATE clips SET {string.Join(", ", sets)} WHERE id = @Id";
        await using var conn2 = _db.OpenConnection();
        await conn2.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    private static async Task UpdateSidecarAsync(string sidecarPath, ClipMetadataPatch patch, CancellationToken ct)
    {
        if (!File.Exists(sidecarPath)) return;

        JsonNode? root;
        await using (var read = File.OpenRead(sidecarPath))
        {
            root = await JsonNode.ParseAsync(read, cancellationToken: ct).ConfigureAwait(false);
        }
        if (root is not JsonObject obj) return;

        if (patch.PlayerName is not null) obj["playerName"] = patch.PlayerName;
        if (patch.PlayerRealm is not null) obj["playerRealm"] = patch.PlayerRealm;
        if (patch.PlayerRegion is not null) obj["playerRegion"] = patch.PlayerRegion;
        if (patch.PlayerClass is not null) obj["playerClass"] = patch.PlayerClass;
        if (patch.PlayerSpec is not null) obj["playerSpec"] = patch.PlayerSpec;
        if (patch.Success is { } success) obj["success"] = success;

        // UnsafeRelaxedJsonEscaping keeps non-ASCII chars unescaped so a name
        // like "Frostytånegl" stays human-readable in the sidecar instead of
        // being mangled to "Frostytånegl".
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        await using var write = new FileStream(sidecarPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(write, obj, options, ct).ConfigureAwait(false);
    }

    public async Task DeleteClipAsync(long clipId, CancellationToken ct)
    {
        await using var conn = _db.OpenConnection();
        var paths = await conn.QuerySingleOrDefaultAsync<(string VideoPath, string SidecarPath)?>(
            new CommandDefinition(
                "SELECT video_path AS VideoPath, sidecar_path AS SidecarPath FROM clips WHERE id = @Id",
                new { Id = clipId }, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM clips WHERE id = @Id",
            new { Id = clipId }, cancellationToken: ct));
        if (paths is { } p)
        {
            TryDelete(p.VideoPath);
            TryDelete(p.SidecarPath);
            // The .events.json sidecar isn't tracked in the DB row; derive its
            // path the same way ClipEventsLoader.EventsPathFor does so old
            // recordings don't leave orphaned timeline files behind.
            TryDelete(Path.ChangeExtension(p.VideoPath, null) + ".events.json");
        }
    }

    public async Task<BulkDeleteResult> DeleteClipsAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new BulkDeleteResult(0, 0);

        await using var conn = _db.OpenConnection();
        // Snapshot paths before the row deletes so we can clean up files on
        // the way out. Dapper's IN @Ids expansion handles the list.
        var paths = (await conn.QueryAsync<(string VideoPath, string SidecarPath)>(
            new CommandDefinition(
                "SELECT video_path AS VideoPath, sidecar_path AS SidecarPath FROM clips WHERE id IN @Ids",
                new { Ids = ids }, cancellationToken: ct))).ToList();

        // One transaction for all row deletes — either every row goes or none
        // do, so the file-cleanup loop below operates against a consistent DB.
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(ct);
        var rowsDeleted = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM clips WHERE id IN @Ids",
            new { Ids = ids }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        // File deletes are best-effort — a permission error on one file
        // shouldn't roll back the whole batch's row deletes. Match the same
        // path-derivation as DeleteClipAsync for the events sidecar.
        int failed = 0;
        foreach (var p in paths)
        {
            if (!TryDeleteCounted(p.VideoPath)) failed++;
            if (!TryDeleteCounted(p.SidecarPath)) failed++;
            if (!TryDeleteCounted(Path.ChangeExtension(p.VideoPath, null) + ".events.json")) failed++;
        }
        return new BulkDeleteResult(rowsDeleted, failed);
    }

    private static void TryDelete(string path) => TryDeleteCounted(path);

    /// <summary>
    /// Returns true on success or no-op (file didn't exist); false if File.Delete threw.
    /// </summary>
    private static bool TryDeleteCounted(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string FormatDate(DateTimeOffset d) =>
        d.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static ClipRecord Map(dynamic row)
    {
        return new ClipRecord(
            Id: (long)row.id,
            VideoPath: (string)row.video_path,
            SidecarPath: (string)row.sidecar_path,
            ContentType: Enum.Parse<ContentType>((string)row.content_type),
            DisplayName: (string)row.display_name,
            Difficulty: row.difficulty as string,
            EncounterId: row.encounter_id is null ? null : (int?)(long)row.encounter_id,
            KeystoneLevel: row.keystone_level is null ? null : (int?)(long)row.keystone_level,
            Success: ((long)row.success) != 0,
            StartedAt: ParseDate((string)row.started_at),
            EndedAt: ParseDate((string)row.ended_at),
            DurationSeconds: (int)(long)row.duration_seconds,
            FileSizeBytes: (long)row.file_size_bytes,
            Favorite: ((long)row.favorite) != 0,
            IndexedAt: ParseDate((string)row.indexed_at),
            PlayerName: row.player_name as string,
            PlayerRealm: row.player_realm as string,
            PlayerRegion: row.player_region as string,
            PlayerClass: row.player_class as string,
            PlayerSpec: row.player_spec as string,
            InstanceName: row.instance_name as string,
            DungeonName: row.dungeon_name as string,
            EventsSidecarPath: row.events_sidecar_path as string,
            HasEvents: ((long)row.has_events) != 0,
            ArenaBracket: row.arena_bracket as string,
            Notes: row.notes as string,
            BossHpPercent: row.boss_hp_percent as double?,
            KeyUpgradeLevel: row.key_upgrade_level is null ? null : (int)(long)row.key_upgrade_level);
    }
}
