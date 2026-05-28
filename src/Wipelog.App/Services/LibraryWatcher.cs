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

using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Wipelog.App.ViewModels;
using Wipelog.Library;

namespace Wipelog.App.Services;

/// <summary>
/// Watches the daemon's output directory and refreshes the clip list when
/// new clips appear. Coalesces multiple FS events into a single refresh via
/// a 500ms debounce.
/// </summary>
public sealed class LibraryWatcher : IDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly IClipRepository _repo;
    private readonly ClipListViewModel _list;
    private readonly ILogger<LibraryWatcher> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private DispatcherTimer? _debounce;

    public LibraryWatcher(
        IClipRepository repo,
        ClipListViewModel list,
        ILogger<LibraryWatcher> logger)
    {
        _repo = repo;
        _list = list;
        _logger = logger;
    }

    public async Task StartAsync(string defaultOutputDirectory)
    {
        var dirs = new HashSet<string>(StringComparer.Ordinal);

        // 1) Default output directory if it exists
        if (Directory.Exists(defaultOutputDirectory))
        {
            dirs.Add(Path.GetFullPath(defaultOutputDirectory));
        }

        // 2) Parent directories of any clips already in the library
        try
        {
            var clips = await _repo.QueryClipsAsync(new ClipQuery(Limit: 200, Offset: 0), CancellationToken.None);
            foreach (var clip in clips)
            {
                var dir = Path.GetDirectoryName(clip.VideoPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    dirs.Add(Path.GetFullPath(dir));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not enumerate existing clip dirs.");
        }

        foreach (var dir in dirs)
        {
            try
            {
                var watcher = new FileSystemWatcher(dir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                };
                watcher.Created += OnFsEvent;
                watcher.Renamed += OnFsEvent;
                watcher.Deleted += OnFsEvent;
                _watchers.Add(watcher);
                _logger.LogInformation("LibraryWatcher: watching {Dir}", dir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not watch {Dir}", dir);
            }
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // Ignore in-progress recordings (hidden temp files).
        var name = Path.GetFileName(e.FullPath);
        if (name.Contains(".recording.mp4", StringComparison.Ordinal)) return;
        if (name.StartsWith('.')) return;
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        try
        {
            if (_debounce is null)
            {
                _debounce = new DispatcherTimer { Interval = DebounceInterval };
                _debounce.Tick += async (_, _) =>
                {
                    _debounce!.Stop();
                    try { await _list.RefreshAsync(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Refresh after FS event failed."); }
                };
            }
            _debounce.Stop();
            _debounce.Start();
        }
        catch (InvalidOperationException)
        {
            // No dispatcher (test context) — skip.
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); }
            catch { /* best-effort */ }
        }
        _watchers.Clear();
        _debounce?.Stop();
    }
}
