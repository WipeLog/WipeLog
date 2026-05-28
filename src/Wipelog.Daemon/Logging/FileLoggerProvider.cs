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

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Wipelog.Daemon.Logging;

/// <summary>
/// Appends host log output to a single rolling text file so the user can
/// <c>tail -f</c> errors without going through <c>journalctl</c>. One-shot
/// rotation at startup: if the existing file is larger than
/// <see cref="MaxFileBytes"/>, it is renamed to <c>&lt;file&gt;.1</c>
/// (overwriting any prior rotation). This keeps disk usage bounded at
/// roughly 2x the cap without the complexity of size-based rotation
/// mid-run. Writes are locked + auto-flushed so a fresh <c>tail</c> picks
/// up new lines immediately.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private readonly StreamWriter _writer;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        RotateIfTooLarge(path);
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    public void Dispose()
    {
        lock (_writeLock)
        {
            _writer.Dispose();
        }
    }

    internal void Write(string line)
    {
        lock (_writeLock)
        {
            _writer.WriteLine(line);
        }
    }

    private static void RotateIfTooLarge(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxFileBytes) return;
            var rotated = path + ".1";
            if (File.Exists(rotated)) File.Delete(rotated);
            File.Move(path, rotated);
        }
        catch
        {
            // Rotation is best-effort; if it fails we just append to the
            // existing file. Failing to start logging would be worse.
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string category, FileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            var sb = new StringBuilder(256);
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.Append(" [").Append(LevelToken(logLevel)).Append("] ");
            sb.Append(_category).Append(": ").Append(message);
            if (exception is not null)
            {
                sb.Append('\n').Append(exception);
            }
            _provider.Write(sb.ToString());
        }

        private static string LevelToken(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT ",
            _ => "?    ",
        };
    }
}
