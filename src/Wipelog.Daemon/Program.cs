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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Wipelog.Core.Time;
using Wipelog.Core.Preferences;
using Wipelog.Daemon;
using Wipelog.Daemon.Configuration;
using Wipelog.Daemon.Control;
using Wipelog.Daemon.Logging;
using Wipelog.Daemon.Notifications;
using Wipelog.Daemon.Status;
using Wipelog.Daemon.Storage;
using Wipelog.Library;

// `Wipelog.Daemon index --output <dir> [--db-path <path>]` reconciles the
// SQLite library with the on-disk sidecars without starting the recorder.
if (args.Length > 0 && args[0] == "index")
{
    return await RunIndexAsync(args.Skip(1).ToArray());
}

// `Wipelog.Daemon backfill-events --output <dir> --log-dir <dir>
// [--log-pattern WoWCombatLog-*.txt] [--db-path <path>]` walks clips lacking
// a .events.json sidecar and replays the log events for their time window
// into a fresh sidecar. Best-effort: clips whose log window has rotated
// away are skipped silently.
if (args.Length > 0 && args[0] == "backfill-events")
{
    return await RunBackfillAsync(args.Skip(1).ToArray());
}

// `Wipelog.Daemon apply-overlay --clip <file> [--output <file>] [--in-place]
// [--x N --y N --width N --height N]` applies the chat-overlay mask to an
// existing clip so users can dial in the region (and confirm hardware encode
// timing) without recording. Region defaults come from preferences.json; CLI
// flags override. Writes a sibling `*.masked.<ext>` by default, preserving the
// original, unless `--in-place` is given.
if (args.Length > 0 && args[0] == "apply-overlay")
{
    return await RunApplyOverlayAsync(args.Skip(1).ToArray());
}

// `--no-notifications` is presence-only. `AddCommandLine` (used by
// `Host.CreateApplicationBuilder`) requires `--key=value`, so strip the bare
// flag here and feed the rest to the host builder.
bool notificationsDisabled = args.Contains("--no-notifications", StringComparer.Ordinal);
args = args.Where(a => a != "--no-notifications").ToArray();

// `--audio` can be repeated to emit multiple mp4 audio tracks (one -a per
// gpu-screen-recorder track). AddCommandLine collapses repeats into one
// value, so we pre-scan, collect all occurrences in order, and remove them
// before handing the rest to the host builder. Both `--audio X` and
// `--audio=X` forms are supported, matching the rest of the daemon CLI.
var audioTracks = new List<string>();
{
    var remaining = new List<string>(args.Length);
    for (int i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (string.Equals(a, "--audio", StringComparison.Ordinal) && i + 1 < args.Length)
        {
            audioTracks.Add(args[++i]);
            continue;
        }
        if (a.StartsWith("--audio=", StringComparison.Ordinal))
        {
            audioTracks.Add(a.Substring("--audio=".Length));
            continue;
        }
        remaining.Add(a);
    }
    args = remaining.ToArray();
}

var builder = Host.CreateApplicationBuilder(args);

string? logPath = builder.Configuration["log-path"];
string? logDir = builder.Configuration["log-dir"];
string logPattern = builder.Configuration["log-pattern"] ?? "WoWCombatLog-*.txt";
string? output = builder.Configuration["output"];
string dbPath = builder.Configuration["db-path"] ?? ClipDatabase.DefaultPath();
if (string.IsNullOrWhiteSpace(output) ||
    (string.IsNullOrWhiteSpace(logPath) && string.IsNullOrWhiteSpace(logDir)) ||
    (!string.IsNullOrWhiteSpace(logPath) && !string.IsNullOrWhiteSpace(logDir)))
{
    Console.Error.WriteLine(
        "usage: Wipelog.Daemon (--log-path <file> | --log-dir <directory> [--log-pattern WoWCombatLog-*.txt]) " +
        "--output <directory> [--db-path <path>] [--player-name <name>] " +
        "[--executable gpu-screen-recorder] [--window focused] [--framerate 60] " +
        "[--codec h264] [--audio default_output [--audio app:spotify ...]] [--container mp4] " +
        "[--video-quality medium|high|very_high|ultra] " +
        "[--video-resolution source|1440p|1080p|720p] " +
        "[--preroll-seconds 30] " +
        "[--postroll-seconds 5] " +
        "[--no-notifications] " +
        "[--log-output-path <file>|off]\n" +
        "       Wipelog.Daemon index --output <directory> [--db-path <path>]");
    return 1;
}

string quality = builder.Configuration["video-quality"] ?? "medium";
var allowedQualities = new[] { "medium", "high", "very_high", "ultra" };
if (!allowedQualities.Contains(quality, StringComparer.Ordinal))
{
    Console.Error.WriteLine(
        $"--video-quality must be one of: {string.Join(", ", allowedQualities)}");
    return 1;
}

// --video-resolution: optional downscale. "source"/null/empty = record at the
// capture monitor's native size. The other choices are target heights paired
// with the monitor's actual aspect ratio at recording time (GpuScreenRecorder
// resolves the width once it can query --list-monitors).
string? videoResolutionRaw = builder.Configuration["video-resolution"];
if (!VideoResolutions.IsValid(videoResolutionRaw))
{
    Console.Error.WriteLine(
        $"--video-resolution must be one of: {string.Join(", ", VideoResolutions.UserChoices)}");
    return 1;
}
int? targetHeight = VideoResolutions.TargetHeight(videoResolutionRaw);

// `--preroll-seconds N`: opt-in pre-encounter buffer. gpu-screen-recorder
// only accepts 2-86400 in replay mode; values outside that range are
// rejected here so a typo doesn't silently disable the feature.
int? preRollSeconds = null;
if (builder.Configuration["preroll-seconds"] is { } preRollRaw &&
    !string.IsNullOrWhiteSpace(preRollRaw))
{
    if (!int.TryParse(preRollRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        || parsed < 2 || parsed > 86400)
    {
        Console.Error.WriteLine(
            "--preroll-seconds must be an integer between 2 and 86400 (gpu-screen-recorder's -r range).");
        return 1;
    }
    preRollSeconds = parsed;
}

// `--postroll-seconds N`: hold the recorder open this many seconds after the
// encounter's stop signal. Plain await Task.Delay — no second process needed.
// Range capped at 300s; longer "tails" likely indicate a config typo.
int? postRollSeconds = null;
if (builder.Configuration["postroll-seconds"] is { } postRollRaw &&
    !string.IsNullOrWhiteSpace(postRollRaw))
{
    if (!int.TryParse(postRollRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        || parsed < 0 || parsed > 300)
    {
        Console.Error.WriteLine(
            "--postroll-seconds must be an integer between 0 and 300.");
        return 1;
    }
    if (parsed > 0) postRollSeconds = parsed;
}

// Tee logs to a single rolling file so the user can `tail -f` without
// going through journalctl. Default: XDG_STATE_HOME/wipelog/daemon.log
// (i.e., ~/.local/state/wipelog/daemon.log). Override with
// --log-output-path. Set to "off" to disable.
string? logOutputPath = builder.Configuration["log-output-path"];
if (!string.Equals(logOutputPath, "off", StringComparison.OrdinalIgnoreCase))
{
    var resolved = string.IsNullOrWhiteSpace(logOutputPath)
        ? DefaultDaemonLogPath()
        : Path.GetFullPath(logOutputPath);
    builder.Logging.AddProvider(new FileLoggerProvider(resolved));
    Console.Error.WriteLine($"Daemon log file: {resolved}");
}

var daemonOptions = new DaemonOptions(
    LogPath: logPath is null ? null : Path.GetFullPath(logPath),
    LogDirectory: logDir is null ? null : Path.GetFullPath(logDir),
    LogPattern: logPattern,
    OutputDirectory: Path.GetFullPath(output),
    DatabasePath: Path.GetFullPath(dbPath),
    Executable: builder.Configuration["executable"] ?? "gpu-screen-recorder",
    Window: builder.Configuration["window"] ?? "focused",
    Framerate: int.TryParse(builder.Configuration["framerate"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fr) ? fr : 60,
    Codec: builder.Configuration["codec"] ?? "h264",
    AudioTracks: audioTracks.Count > 0
        ? audioTracks
        : new[] { "default_output" },
    Container: builder.Configuration["container"] ?? "mp4",
    Quality: quality,
    TargetHeight: targetHeight,
    PlayerName: builder.Configuration["player-name"],
    PreRollSeconds: preRollSeconds,
    PostRollSeconds: postRollSeconds);

builder.Services.AddSingleton(daemonOptions);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<CombatLogParser>();
builder.Services.AddSingleton(sp =>
{
    var parser = sp.GetRequiredService<CombatLogParser>();
    return daemonOptions.LogDirectory is not null
        ? new LogTailer(daemonOptions.LogDirectory, daemonOptions.LogPattern, parser)
        : new LogTailer(daemonOptions.LogPath!, parser);
});
// Preferences are read once at startup (file-on-startup model — no live
// reload). Used for both the recording content filters below and the
// notification toggles further down.
var userPrefs = new UserPreferencesService().Load();
builder.Services.AddSingleton(sp => new EncounterDetector(
    filters: userPrefs.RecordingFilters,
    logger: sp.GetRequiredService<ILogger<EncounterDetector>>()));
// Hot-reload the recording filters when the GUI rewrites preferences.json, so
// CONTENT settings (keystone minimum, raid difficulties, BG toggle) apply
// without a daemon restart. Other settings stay baked into the CLI args.
builder.Services.AddHostedService(sp => new RecordingFilterReloader(
    sp.GetRequiredService<EncounterDetector>(),
    sp.GetRequiredService<ILogger<RecordingFilterReloader>>()));
builder.Services.AddSingleton<IClipNamingStrategy, DefaultClipNamingStrategy>();
builder.Services.AddSingleton(new GpuScreenRecorderOptions(
    ExecutablePath: daemonOptions.Executable,
    Window: daemonOptions.Window,
    Framerate: daemonOptions.Framerate,
    Codec: daemonOptions.Codec,
    AudioTracks: daemonOptions.AudioTracks,
    Container: daemonOptions.Container,
    Quality: daemonOptions.Quality,
    TargetHeight: daemonOptions.TargetHeight));
builder.Services.AddSingleton<IRecorder, GpuScreenRecorder>();

// Pre-roll: opt-in via --preroll-seconds. A second gsr instance buffers the
// last N seconds in RAM; the RecorderController flushes it on every
// ENCOUNTER_START and ffmpeg-concats it onto the front of the finalized
// clip. Transient files live in `<output>/.preroll/`, swept on each
// successful concat. The hidden ("." prefix) name keeps them out of the
// indexer (TopDirectoryOnly) and the library watcher (IncludeSubdirectories
// = false).
if (daemonOptions.PreRollSeconds is { } prerollN)
{
    var preRollDir = Path.Combine(daemonOptions.OutputDirectory, ".preroll");
    builder.Services.AddSingleton<IPreRollRecorder>(sp =>
    {
        var preRollRecorder = new GpuScreenRecorder(
            new GpuScreenRecorderOptions(
                ExecutablePath: daemonOptions.Executable,
                Window: daemonOptions.Window,
                Framerate: daemonOptions.Framerate,
                Codec: daemonOptions.Codec,
                AudioTracks: daemonOptions.AudioTracks,
                Container: daemonOptions.Container,
                Quality: daemonOptions.Quality,
                ReplayBufferSeconds: prerollN,
                TargetHeight: daemonOptions.TargetHeight),
            sp.GetRequiredService<ILogger<GpuScreenRecorder>>());
        return new PreRollRecorder(
            preRollRecorder,
            preRollDir,
            sp.GetRequiredService<ILogger<PreRollRecorder>>());
    });
}

builder.Services.AddSingleton(sp => new ClipDatabase(
    daemonOptions.DatabasePath,
    sp.GetRequiredService<ILogger<ClipDatabase>>()));
builder.Services.AddSingleton<IClipRepository, ClipRepository>();
builder.Services.AddSingleton<ILibraryIndexer, LibraryIndexer>();
builder.Services.AddSingleton<IClipRecorderSink, LibrarySink>();

builder.Services.AddSingleton<HeartbeatState>();
builder.Services.AddSingleton<IAdvancedLoggingObserver, AdvancedLoggingMonitor>();
builder.Services.AddSingleton<INotifySendInvoker, NotifySendInvoker>();
// `--no-notifications` is a kill switch that beats the preferences file.
// Otherwise the daemon picks up the GUI's notification toggles from
// ~/.config/wipelog/preferences.json (loaded once above as `userPrefs`).
builder.Services.AddSingleton(notificationsDisabled
    ? NotificationOptions.AllDisabled
    : new NotificationOptions(userPrefs.NotifyOnClipSaved, userPrefs.NotifyOnErrors));
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<IRecordingLifecycleObserver, DaemonLifecycleObserver>();

// Disk management: a single DiskMonitor instance enforces the storage cap on
// the recording-start path (as IRecordingStartGate) and is polled by the
// periodic DiskWarningMonitor for the 80% desktop warning. The cap comes from
// preferences.json (loaded once above) — no live reload.
builder.Services.AddSingleton(sp => new DiskMonitor(
    daemonOptions.OutputDirectory,
    DiskMonitor.GigabytesToBytes(userPrefs.StorageManagement.MinFreeGigabytes),
    sp.GetRequiredService<IClipRepository>(),
    sp.GetRequiredService<INotificationService>(),
    sp.GetRequiredService<HeartbeatState>(),
    Wipelog.Core.Storage.SystemDiskSpaceProbe.Instance,
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<ILogger<DiskMonitor>>()));
builder.Services.AddSingleton<IDiskMonitor>(sp => sp.GetRequiredService<DiskMonitor>());
builder.Services.AddSingleton<IRecordingStartGate>(sp => sp.GetRequiredService<DiskMonitor>());
builder.Services.AddHostedService(sp => new DiskWarningMonitor(
    sp.GetRequiredService<IDiskMonitor>(),
    sp.GetRequiredService<INotificationService>(),
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<ILogger<DiskWarningMonitor>>()));

builder.Services.AddSingleton(sp => new HeartbeatWriter(
    sp.GetRequiredService<HeartbeatState>(),
    sp.GetRequiredService<ILogger<HeartbeatWriter>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<HeartbeatWriter>());

builder.Services.AddSingleton(sp => new RecorderController(
    sp.GetRequiredService<EncounterDetector>(),
    sp.GetRequiredService<IRecorder>(),
    sp.GetRequiredService<IClipNamingStrategy>(),
    daemonOptions.OutputDirectory,
    sp.GetRequiredService<ILogger<RecorderController>>(),
    sp.GetRequiredService<IClipRecorderSink>(),
    daemonOptions.PlayerName,
    sp.GetRequiredService<ILogger<PlayerSpecResolver>>(),
    sp.GetRequiredService<IRecordingLifecycleObserver>(),
    sp.GetService<IPreRollRecorder>(),
    sp.GetService<IAdvancedLoggingObserver>(),
    daemonOptions.AudioTracks,
    sp.GetService<IRecordingStartGate>(),
    daemonOptions.PostRollSeconds ?? 0,
    userPrefs.ChatOverlay,
    captureExecutable: daemonOptions.Executable,
    captureTarget: daemonOptions.Window));
builder.Services.AddHostedService<WipelogHostedService>();

// Control socket — listens for GUI-issued commands (currently just
// "abortRecording" for the Stop button). Path is derived from
// $XDG_RUNTIME_DIR so the App can connect without extra configuration.
builder.Services.AddHostedService(sp => new ControlServer(
    sp.GetRequiredService<RecorderController>(),
    sp.GetRequiredService<ILogger<ControlServer>>()));

var host = builder.Build();
await host.RunAsync();
return 0;

static string DefaultDaemonLogPath()
{
    // Follow the XDG base-dir spec for state data; fall back to ~/.local/state
    // when the env var is unset (the common case on most Linux distros).
    var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
    if (string.IsNullOrWhiteSpace(stateHome))
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        stateHome = Path.Combine(home, ".local", "state");
    }
    return Path.Combine(stateHome, "wipelog", "daemon.log");
}

static async Task<int> RunIndexAsync(string[] args)
{
    var config = new ConfigurationBuilder().AddCommandLine(args).Build();
    var output = config["output"];
    var dbPath = config["db-path"] ?? ClipDatabase.DefaultPath();
    if (string.IsNullOrWhiteSpace(output))
    {
        Console.Error.WriteLine("usage: Wipelog.Daemon index --output <directory> [--db-path <path>]");
        return 1;
    }
    using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    var db = new ClipDatabase(Path.GetFullPath(dbPath), loggerFactory.CreateLogger<ClipDatabase>());
    var repo = new ClipRepository(db);
    var indexer = new LibraryIndexer(repo, loggerFactory.CreateLogger<LibraryIndexer>());
    var result = await indexer.ReconcileAsync(Path.GetFullPath(output), CancellationToken.None);
    Console.WriteLine($"Indexer: added {result.ClipsAdded}, removed {result.ClipsRemoved}, errors {result.Errors}.");
    return result.Errors > 0 ? 2 : 0;
}

static async Task<int> RunBackfillAsync(string[] args)
{
    // `--force` is presence-only. AddCommandLine doesn't parse bare flags
    // by default, so accept it manually before binding the rest.
    bool force = args.Contains("--force", StringComparer.Ordinal);
    var withoutForce = args.Where(a => a != "--force").ToArray();
    var config = new ConfigurationBuilder().AddCommandLine(withoutForce).Build();
    var output = config["output"];
    var logDir = config["log-dir"];
    var logPattern = config["log-pattern"] ?? "WoWCombatLog-*.txt";
    var dbPath = config["db-path"] ?? ClipDatabase.DefaultPath();
    if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(logDir))
    {
        Console.Error.WriteLine(
            "usage: Wipelog.Daemon backfill-events --output <directory> " +
            "--log-dir <directory> [--log-pattern WoWCombatLog-*.txt] [--db-path <path>] [--force]");
        return 1;
    }

    using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    var backfiller = new EventsBackfiller(loggerFactory.CreateLogger<EventsBackfiller>());
    var result = await backfiller.RunAsync(
        Path.GetFullPath(output), Path.GetFullPath(logDir), logPattern, CancellationToken.None, force);
    Console.WriteLine(
        $"Backfill: wrote {result.Written} ({result.Overwritten} overwritten), " +
        $"skipped (no log data) {result.Skipped}, errors {result.Errors}.");

    // Re-run the indexer so the has_events flag picks up the newly written
    // sidecars in the DB.
    var db = new ClipDatabase(Path.GetFullPath(dbPath), loggerFactory.CreateLogger<ClipDatabase>());
    var repo = new ClipRepository(db);
    var indexer = new LibraryIndexer(repo, loggerFactory.CreateLogger<LibraryIndexer>());
    var ix = await indexer.ReconcileAsync(Path.GetFullPath(output), CancellationToken.None);
    Console.WriteLine($"Indexer: added {ix.ClipsAdded}, removed {ix.ClipsRemoved}, errors {ix.Errors}.");
    return result.Errors > 0 || ix.Errors > 0 ? 2 : 0;
}

static async Task<int> RunApplyOverlayAsync(string[] args)
{
    // `--in-place` is presence-only; AddCommandLine doesn't bind bare flags.
    bool inPlace = args.Contains("--in-place", StringComparer.Ordinal);
    var rest = args.Where(a => a != "--in-place").ToArray();
    var config = new ConfigurationBuilder().AddCommandLine(rest).Build();

    var clip = config["clip"];
    if (string.IsNullOrWhiteSpace(clip) || !File.Exists(clip))
    {
        Console.Error.WriteLine(
            "usage: Wipelog.Daemon apply-overlay --clip <file> [--output <file>] [--in-place] " +
            "[--x N] [--y N] [--width N] [--height N]\n" +
            "       Region defaults to the chatOverlay block in preferences.json; flags override.\n" +
            "       Without --in-place the masked clip is written next to the source as *.masked.<ext>.");
        return 1;
    }
    clip = Path.GetFullPath(clip);

    static int IntOr(string? s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    var defaults = new UserPreferencesService().Load().ChatOverlay;
    var overlay = new ChatOverlay
    {
        Enabled = true, // explicit test — apply regardless of the saved toggle
        X = IntOr(config["x"], defaults.X),
        Y = IntOr(config["y"], defaults.Y),
        Width = IntOr(config["width"], defaults.Width),
        Height = IntOr(config["height"], defaults.Height),
    };
    if (overlay.Width <= 0 || overlay.Height <= 0)
    {
        Console.Error.WriteLine("Width and height must be greater than 0.");
        return 1;
    }

    string target;
    if (inPlace)
    {
        target = clip;
    }
    else
    {
        var output = config["output"];
        if (string.IsNullOrWhiteSpace(output))
        {
            var dir = Path.GetDirectoryName(clip) ?? ".";
            var stem = Path.GetFileNameWithoutExtension(clip);
            target = Path.Combine(dir, $"{stem}.masked{Path.GetExtension(clip)}");
        }
        else
        {
            target = Path.GetFullPath(output);
        }
        File.Copy(clip, target, overwrite: true);
    }

    using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
    var logger = loggerFactory.CreateLogger("ChatOverlay");
    Console.WriteLine(
        $"Applying overlay (x={overlay.X} y={overlay.Y} w={overlay.Width} h={overlay.Height}) to {target} …");

    var ok = await ChatOverlayProcessor.ApplyAsync(target, overlay, logger, CancellationToken.None);
    if (ok)
    {
        Console.WriteLine($"Done. Masked clip: {target}");
        return 0;
    }

    Console.Error.WriteLine("Chat overlay failed (see log above); original preserved.");
    if (!inPlace)
    {
        try { if (File.Exists(target)) File.Delete(target); } catch { /* best-effort */ }
    }
    return 2;
}
