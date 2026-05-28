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

using System.Text;
using System.Threading.Channels;

namespace Wipelog.Core.CombatLog;

/// <summary>
/// Watches a combat-log file with <see cref="FileSystemWatcher"/> and pushes
/// parsed events into an unbounded <see cref="Channel{T}"/>. Tracks a byte
/// offset, buffers partial lines, and resets on truncation or replacement.
///
/// Two modes:
///   File mode — point at one specific file. Tail-from-end on existing
///   content; only new appends are emitted.
///
///   Directory mode — point at a directory and a glob pattern (e.g.
///   <c>WoWCombatLog-*.txt</c>). On startup the newest matching file is
///   tailed from end (so an existing closed session doesn't replay).
///   Whenever a *newer* file appears, the tailer switches to it and reads
///   it from byte 0 — exactly what we need for WoW, which writes a fresh
///   dated file every session.
/// </summary>
/// <remarks>
/// <see cref="Channel{T}"/> is preferred over <see cref="IObservable{T}"/>
/// because consumption is back-pressure-aware via async iteration and is
/// the .NET 10 idiomatic primitive for producer/consumer pipelines.
/// </remarks>
public sealed class LogTailer : IAsyncDisposable
{
    private readonly string? _staticFilePath;
    private readonly string? _watchDirectory;
    private readonly string? _filePattern;
    private readonly CombatLogParser _parser;
    private readonly Channel<CombatLogEvent> _events;
    private readonly Channel<bool> _wakeup;
    private readonly SemaphoreSlim _readGate = new(1, 1);

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cts;
    private Task? _readerTask;
    private string? _currentFile;
    private long _offset;
    private string _partial = string.Empty;

    /// <summary>File mode — tail one specific path.</summary>
    public LogTailer(string path, CombatLogParser parser)
    {
        _staticFilePath = Path.GetFullPath(path);
        _parser = parser;
        (_events, _wakeup) = CreateChannels();
    }

    /// <summary>
    /// Directory mode — auto-tail the newest file matching <paramref name="filePattern"/>
    /// in <paramref name="directory"/>, switching when a newer file appears.
    /// </summary>
    public LogTailer(string directory, string filePattern, CombatLogParser parser)
    {
        _watchDirectory = Path.GetFullPath(directory);
        _filePattern = filePattern;
        _parser = parser;
        (_events, _wakeup) = CreateChannels();
    }

    private static (Channel<CombatLogEvent>, Channel<bool>) CreateChannels() =>
        (
            Channel.CreateUnbounded<CombatLogEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            }),
            Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            })
        );

    public ChannelReader<CombatLogEvent> Events => _events.Reader;

    public Task StartAsync(CancellationToken ct)
    {
        if (_readerTask is not null)
        {
            throw new InvalidOperationException("LogTailer already started.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        string watchDir;
        string watchFilter;
        if (_staticFilePath is not null)
        {
            // File mode: pre-seed offset to file end so existing content isn't replayed.
            _currentFile = _staticFilePath;
            if (File.Exists(_currentFile))
            {
                _offset = new FileInfo(_currentFile).Length;
            }
            watchDir = Path.GetDirectoryName(_currentFile) ?? ".";
            watchFilter = Path.GetFileName(_currentFile);
        }
        else
        {
            // Directory mode: pick the newest matching file (tail from end).
            // Newer files appearing later are picked up via FSW Created events
            // and tailed from byte 0.
            var newest = FindNewestMatching(_watchDirectory!, _filePattern!);
            if (newest is not null)
            {
                _currentFile = newest;
                _offset = new FileInfo(newest).Length;
            }
            watchDir = _watchDirectory!;
            watchFilter = _filePattern!;
        }

        _watcher = new FileSystemWatcher(watchDir, watchFilter)
        {
            NotifyFilter = NotifyFilters.LastWrite
                         | NotifyFilters.Size
                         | NotifyFilters.FileName
                         | NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;

        _readerTask = Task.Run(() => ReaderLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        _wakeup.Writer.TryWrite(true);

    private async Task ReaderLoopAsync(CancellationToken ct)
    {
        try
        {
            await ReadAvailableAsync(ct).ConfigureAwait(false);
            while (await _wakeup.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_wakeup.Reader.TryRead(out _)) { }
                await ReadAvailableAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _events.Writer.TryComplete();
        }
    }

    private async Task ReadAvailableAsync(CancellationToken ct)
    {
        await _readGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Directory mode: did a newer file appear? Switch to it and read from byte 0.
            if (_watchDirectory is not null)
            {
                var newest = FindNewestMatching(_watchDirectory, _filePattern!);
                if (newest is not null && !string.Equals(newest, _currentFile, StringComparison.Ordinal))
                {
                    _currentFile = newest;
                    _offset = 0;
                    _partial = string.Empty;
                }
            }

            if (_currentFile is null || !File.Exists(_currentFile))
            {
                return;
            }

            long length;
            try
            {
                length = new FileInfo(_currentFile).Length;
            }
            catch (FileNotFoundException)
            {
                return;
            }

            if (length < _offset)
            {
                _offset = 0;
                _partial = string.Empty;
            }

            if (length == _offset)
            {
                return;
            }

            await using var fs = new FileStream(
                _currentFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            fs.Position = _offset;
            int toRead = checked((int)Math.Min(length - _offset, int.MaxValue));
            var buffer = new byte[toRead];
            int read = 0;
            while (read < toRead)
            {
                int n = await fs.ReadAsync(buffer.AsMemory(read, toRead - read), ct).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            _offset += read;

            if (read == 0) return;

            string chunk = Encoding.UTF8.GetString(buffer, 0, read);
            string combined = _partial + chunk;
            int lastNewline = combined.LastIndexOf('\n');
            if (lastNewline < 0)
            {
                _partial = combined;
                return;
            }

            _partial = combined[(lastNewline + 1)..];
            string complete = combined[..(lastNewline + 1)];

            int start = 0;
            for (int i = 0; i < complete.Length; i++)
            {
                if (complete[i] != '\n') continue;
                int end = i;
                if (end > start && complete[end - 1] == '\r') end--;
                if (end > start)
                {
                    var line = complete.AsSpan(start, end - start);
                    var evt = _parser.TryParseLine(line);
                    if (evt is not null)
                    {
                        await _events.Writer.WriteAsync(evt, ct).ConfigureAwait(false);
                    }
                }
                start = i + 1;
            }
        }
        finally
        {
            _readGate.Release();
        }
    }

    private static string? FindNewestMatching(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return null;
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, pattern);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        if (files.Length == 0) return null;

        string newest = files[0];
        var newestTime = File.GetLastWriteTimeUtc(newest);
        for (int i = 1; i < files.Length; i++)
        {
            var t = File.GetLastWriteTimeUtc(files[i]);
            if (t > newestTime)
            {
                newest = files[i];
                newestTime = t;
            }
        }
        return newest;
    }

    public async ValueTask DisposeAsync()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileEvent;
            _watcher.Created -= OnFileEvent;
            _watcher.Renamed -= OnFileEvent;
            _watcher.Dispose();
            _watcher = null;
        }

        _cts?.Cancel();
        _wakeup.Writer.TryComplete();

        if (_readerTask is not null)
        {
            try { await _readerTask.ConfigureAwait(false); }
            catch { /* swallow shutdown exceptions */ }
            _readerTask = null;
        }

        _events.Writer.TryComplete();
        _cts?.Dispose();
        _cts = null;
        _readGate.Dispose();
    }
}
