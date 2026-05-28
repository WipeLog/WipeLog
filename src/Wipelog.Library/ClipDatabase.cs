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

using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Wipelog.Library;

/// <summary>
/// Owns the SQLite database file path and the migration runner. Hand out
/// connections via <see cref="OpenConnection"/>; every connection is
/// configured for WAL with foreign keys on.
/// </summary>
public sealed class ClipDatabase
{
    private const int CurrentSchemaVersion = 8;

    private readonly string _path;
    private readonly ILogger<ClipDatabase> _logger;
    private bool _migrated;
    private readonly object _migrationGate = new();

    public ClipDatabase(string path, ILogger<ClipDatabase> logger)
    {
        _path = path;
        _logger = logger;
    }

    public string Path => _path;

    public SqliteConnection OpenConnection()
    {
        EnsureMigrated();
        var conn = new SqliteConnection(BuildConnectionString(_path));
        conn.Open();
        ApplyConnectionPragmas(conn);
        return conn;
    }

    private static string BuildConnectionString(string path) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

    private static void ApplyConnectionPragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            """;
        cmd.ExecuteNonQuery();
    }

    private void EnsureMigrated()
    {
        if (_migrated) return;
        lock (_migrationGate)
        {
            if (_migrated) return;
            EnsureDirectory(_path);
            using var conn = new SqliteConnection(BuildConnectionString(_path));
            conn.Open();
            ApplyConnectionPragmas(conn);
            int version = ReadUserVersion(conn);
            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    "Database schema is newer than this build supports.");
            }
            if (version < CurrentSchemaVersion)
            {
                _logger.LogInformation(
                    "Migrating clips DB at {Path}: schema {Old} -> {New}",
                    _path, version, CurrentSchemaVersion);
                RunMigrations(conn, version);
            }
            _migrated = true;
        }
    }

    private static int ReadUserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var v = cmd.ExecuteScalar();
        return v switch
        {
            long l => (int)l,
            int i => i,
            _ => 0,
        };
    }

    private static void RunMigrations(SqliteConnection conn, int from)
    {
        for (int next = from + 1; next <= CurrentSchemaVersion; next++)
        {
            string sql = LoadEmbeddedMigration(next);
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    private static string LoadEmbeddedMigration(int number)
    {
        var asm = typeof(ClipDatabase).Assembly;
        string suffix = $"Migrations.{number:D3}_";
        string? resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.Contains(suffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded migration {number:D3} not found.");
        }
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void EnsureDirectory(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public static string DefaultPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        var baseDir = string.IsNullOrEmpty(xdg)
            ? System.IO.Path.Combine(home, ".local", "share")
            : xdg;
        return System.IO.Path.Combine(baseDir, "wipelog", "clips.db");
    }
}
