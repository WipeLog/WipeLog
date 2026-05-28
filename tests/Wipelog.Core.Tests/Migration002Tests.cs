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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class Migration002Tests
{
    private static string[] PlayerColumns =>
        new[] { "player_name", "player_realm", "player_class", "player_spec",
                "instance_name", "dungeon_name" };

    [Fact]
    public void Fresh_db_migrates_to_current_with_all_new_columns()
    {
        using var dir = new TempDirectory();
        var db = new ClipDatabase(Path.Combine(dir.Path, "fresh.db"), NullLogger<ClipDatabase>.Instance);
        using var conn = db.OpenConnection();

        Assert.Equal(8, ReadUserVersion(conn));
        var cols = ListColumns(conn, "clips");
        foreach (var c in PlayerColumns)
        {
            Assert.Contains(c, cols);
        }
        Assert.Contains("player_region", cols);
        Assert.Contains("events_sidecar_path", cols);
        Assert.Contains("has_events", cols);
        Assert.Contains("arena_bracket", cols);
        Assert.Contains("notes", cols);
        Assert.Contains("boss_hp_percent", cols);
        Assert.Contains("key_upgrade_level", cols);
    }

    [Fact]
    public void V1_db_upgrades_through_v2_to_current_preserving_existing_rows()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "v1.db");

        // Hand-roll a v1 schema using the SQL from migration 001 directly,
        // then insert a row to verify it survives the 1->2 upgrade.
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE clips (
                    id INTEGER PRIMARY KEY,
                    video_path TEXT NOT NULL UNIQUE,
                    sidecar_path TEXT NOT NULL,
                    content_type TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    difficulty TEXT,
                    encounter_id INTEGER,
                    keystone_level INTEGER,
                    success INTEGER NOT NULL,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NOT NULL,
                    duration_seconds INTEGER NOT NULL,
                    file_size_bytes INTEGER NOT NULL,
                    favorite INTEGER NOT NULL DEFAULT 0,
                    indexed_at TEXT NOT NULL
                );
                INSERT INTO clips (video_path, sidecar_path, content_type, display_name, success,
                                   started_at, ended_at, duration_seconds, file_size_bytes, indexed_at)
                VALUES ('/v1.mp4', '/v1.json', 'RaidEncounter', 'Boss', 1,
                        '2026-05-01T10:00:00+00:00', '2026-05-01T10:05:00+00:00', 300, 1, '2026-05-01T10:05:00+00:00');
                PRAGMA user_version = 1;
                """;
            cmd.ExecuteNonQuery();
        }

        var db = new ClipDatabase(path, NullLogger<ClipDatabase>.Instance);
        using var verify = db.OpenConnection();
        Assert.Equal(8, ReadUserVersion(verify));
        var cols = ListColumns(verify, "clips");
        foreach (var c in PlayerColumns) Assert.Contains(c, cols);
        Assert.Contains("player_region", cols);
        Assert.Contains("events_sidecar_path", cols);
        Assert.Contains("has_events", cols);
        Assert.Contains("arena_bracket", cols);
        Assert.Contains("notes", cols);

        // Existing row still there.
        using var count = verify.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM clips";
        Assert.Equal(1L, (long)count.ExecuteScalar()!);
    }

    private static int ReadUserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        var v = cmd.ExecuteScalar();
        return v switch { long l => (int)l, int i => i, _ => 0 };
    }

    private static HashSet<string> ListColumns(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        var set = new HashSet<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            set.Add(reader.GetString(reader.GetOrdinal("name")));
        }
        return set;
    }
}
