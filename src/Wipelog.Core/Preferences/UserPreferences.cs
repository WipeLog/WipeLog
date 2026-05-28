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
using Wipelog.Core.Detection;

namespace Wipelog.Core.Preferences;

/// <summary>
/// On-disk contract for the user-tweakable preferences shared between the GUI
/// and the daemon. Lives at <c>$XDG_CONFIG_HOME/wipelog/preferences.json</c>
/// (defaults to <c>~/.config/wipelog/preferences.json</c>).
/// </summary>
public sealed record UserPreferences
{
    [JsonPropertyName("closeToTray")]
    public bool CloseToTray { get; init; } = true;

    [JsonPropertyName("notifyOnClipSaved")]
    public bool NotifyOnClipSaved { get; init; } = true;

    [JsonPropertyName("notifyOnErrors")]
    public bool NotifyOnErrors { get; init; } = true;

    [JsonPropertyName("testRecordingSeconds")]
    public int TestRecordingSeconds { get; init; } = 5;

    [JsonPropertyName("daemon")]
    public DaemonConfig Daemon { get; init; } = DaemonConfig.WithHomeDefaults();

    /// <summary>
    /// Content-type recording filters consulted by the daemon's encounter
    /// detector. Defaults record everything; missing block in an older
    /// preferences file deserializes to these same defaults.
    /// </summary>
    [JsonPropertyName("recordingFilters")]
    public RecordingFilters RecordingFilters { get; init; } = new();

    /// <summary>
    /// Disk free-space policy enforced by the daemon. Missing block in an older
    /// preferences file deserializes to the defaults (keep 50 GB free).
    /// </summary>
    [JsonPropertyName("storageManagement")]
    public StorageManagement StorageManagement { get; init; } = new();

    /// <summary>
    /// Optional chat-region masking applied by the daemon after each clip is
    /// saved. Disabled by default; missing block in an older preferences file
    /// deserializes to the defaults (off).
    /// </summary>
    [JsonPropertyName("chatOverlay")]
    public ChatOverlay ChatOverlay { get; init; } = new();
}

/// <summary>
/// Opt-in privacy mask. When <see cref="Enabled"/>, the daemon composites a
/// solid black rectangle over the configured screen region of every newly
/// saved clip (an ffmpeg <c>drawbox</c> post-processing pass) so chat, whispers,
/// and member names are hidden from clips shared with a guild. The rectangle is
/// in screen pixels: <see cref="X"/> from the left edge, <see cref="Y"/> from the
/// <b>bottom</b> edge (WoW's chat sits bottom-left, so <c>Y = 0</c> means flush
/// with the bottom). Default is off, so users who never share pay no
/// post-processing cost.
/// </summary>
public sealed record ChatOverlay
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = false;

    [JsonPropertyName("x")]
    public int X { get; init; } = 0;

    /// <summary>Pixels from the bottom edge of the screen to the box's bottom edge.</summary>
    [JsonPropertyName("y")]
    public int Y { get; init; } = 0;

    [JsonPropertyName("width")]
    public int Width { get; init; } = 540;

    [JsonPropertyName("height")]
    public int Height { get; init; } = 320;
}

/// <summary>
/// Disk free-space policy. The daemon keeps at least <see cref="MinFreeGigabytes"/>
/// free on the drive holding the clips: it warns as free space approaches the
/// floor and auto-deletes the oldest non-favorited clips on the next recording
/// attempt whenever free space has dropped below it (no matter what consumed the
/// space). Favorited clips are never auto-deleted.
/// </summary>
public sealed record StorageManagement
{
    [JsonPropertyName("minFreeGigabytes")]
    public long MinFreeGigabytes { get; init; } = 50;

    /// <summary>
    /// Legacy key from when this number meant a clip-usage cap. Read only so an
    /// existing preferences file carries its value into <see cref="MinFreeGigabytes"/>;
    /// not written once migrated (null). See <see cref="UserPreferencesService.Load"/>.
    /// </summary>
    [JsonPropertyName("maxStorageGigabytes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LegacyMaxStorageGigabytes { get; init; }
}

/// <summary>
/// Daemon-side options that the GUI writes into the systemd unit on install.
/// Empty strings are treated as "not yet configured" — the install button is
/// disabled until <see cref="LogDirectory"/> and <see cref="OutputDirectory"/>
/// are non-empty.
/// </summary>
public sealed record DaemonConfig
{
    /// <summary>Full path to the <c>Wipelog.Daemon</c> binary (no %h).</summary>
    [JsonPropertyName("daemonExecutablePath")]
    public string DaemonExecutablePath { get; init; } = "";

    [JsonPropertyName("logDirectory")]
    public string LogDirectory { get; init; } = "";

    [JsonPropertyName("logPattern")]
    public string LogPattern { get; init; } = "WoWCombatLog-*.txt";

    [JsonPropertyName("outputDirectory")]
    public string OutputDirectory { get; init; } = "";

    [JsonPropertyName("databasePath")]
    public string DatabasePath { get; init; } = "";

    [JsonPropertyName("gpuScreenRecorderExecutable")]
    public string GpuScreenRecorderExecutable { get; init; } = "gpu-screen-recorder";

    [JsonPropertyName("captureTarget")]
    public string CaptureTarget { get; init; } = "focused";

    [JsonPropertyName("framerate")]
    public int Framerate { get; init; } = 60;

    [JsonPropertyName("codec")]
    public string Codec { get; init; } = "h264";

    [JsonPropertyName("container")]
    public string Container { get; init; } = "mp4";

    /// <summary>
    /// Legacy single-source field. Kept for reading old preferences files; new
    /// writes use <see cref="AudioTracks"/>. When loading, this is merged into
    /// the tracks list if present and tracks is empty.
    /// </summary>
    [JsonPropertyName("audioSource")]
    public string? AudioSource { get; init; }

    /// <summary>
    /// One entry per audio track in the recording. Each entry is passed verbatim
    /// as a separate <c>-a</c> flag to gpu-screen-recorder, producing one mp4
    /// audio track per entry. Empty list falls back to a single
    /// <c>default_output</c> track at recording time.
    /// </summary>
    [JsonPropertyName("audioTracks")]
    public IReadOnlyList<string> AudioTracks { get; init; } = Array.Empty<string>();

    [JsonPropertyName("videoQuality")]
    public string VideoQuality { get; init; } = "medium";

    /// <summary>
    /// Target output resolution. Recognised values: <c>"source"</c> /
    /// <c>null</c> / empty (no scaling — record at the capture monitor's
    /// native size), <c>"720p"</c>, <c>"1080p"</c>, <c>"1440p"</c>. The daemon
    /// derives the encoded width from the capture monitor's aspect ratio at
    /// runtime so ultrawide displays stay un-letterboxed.
    /// </summary>
    [JsonPropertyName("videoResolution")]
    public string? VideoResolution { get; init; }

    /// <summary>
    /// Opt-in pre-encounter buffer. When set, the daemon launches a second
    /// gpu-screen-recorder in replay mode that keeps the last N seconds in
    /// RAM and prepends them to each clip. Null = disabled; gpu-screen-
    /// recorder's <c>-r</c> accepts 2..86400.
    /// </summary>
    [JsonPropertyName("prerollSeconds")]
    public int? PreRollSeconds { get; init; }

    /// <summary>
    /// Opt-in post-encounter tail. When set, the daemon holds the recorder
    /// open for N seconds after the encounter's stop signal so the saved
    /// clip captures the immediate aftermath (kill cam, wipe panic, key
    /// chest opening). Null = disabled. Sensible range is 0..300.
    /// </summary>
    [JsonPropertyName("postrollSeconds")]
    public int? PostRollSeconds { get; init; }

    public static DaemonConfig WithHomeDefaults()
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        return new DaemonConfig
        {
            DaemonExecutablePath = System.IO.Path.Combine(home, "bin", "wipelog", "Wipelog.Daemon"),
            OutputDirectory = System.IO.Path.Combine(home, "Videos", "wow"),
            DatabasePath = System.IO.Path.Combine(home, ".local", "share", "wipelog", "clips.db"),
        };
    }

    public bool IsValidForInstall(out string? error)
    {
        if (string.IsNullOrWhiteSpace(DaemonExecutablePath))
        {
            error = "Daemon binary path is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(LogDirectory))
        {
            error = "Combat-log directory is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            error = "Output directory is required.";
            return false;
        }
        if (Framerate is < 24 or > 240)
        {
            error = "Framerate must be between 24 and 240.";
            return false;
        }
        var allowedQualities = new[] { "medium", "high", "very_high", "ultra" };
        if (Array.IndexOf(allowedQualities, VideoQuality) < 0)
        {
            error = $"Video quality must be one of: {string.Join(", ", allowedQualities)}";
            return false;
        }
        if (!VideoResolutions.IsValid(VideoResolution))
        {
            error = $"Video resolution must be one of: {string.Join(", ", VideoResolutions.UserChoices)}";
            return false;
        }
        if (PreRollSeconds is { } pr && (pr < 2 || pr > 86400))
        {
            error = "Pre-roll seconds must be between 2 and 86400.";
            return false;
        }
        if (PostRollSeconds is { } po && (po < 0 || po > 300))
        {
            error = "Post-roll seconds must be between 0 and 300.";
            return false;
        }
        error = null;
        return true;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UserPreferences))]
internal partial class UserPreferencesJsonContext : JsonSerializerContext { }

public sealed class UserPreferencesService
{
    private readonly string _path;

    public UserPreferencesService() : this(DefaultPath()) { }

    public UserPreferencesService(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public static string DefaultPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        var baseDir = string.IsNullOrEmpty(xdg) ? System.IO.Path.Combine(home, ".config") : xdg;
        return System.IO.Path.Combine(baseDir, "wipelog", "preferences.json");
    }

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return new UserPreferences();
            using var stream = File.OpenRead(_path);
            var read = JsonSerializer.Deserialize(stream, UserPreferencesJsonContext.Default.UserPreferences)
                ?? new UserPreferences();
            // Source-gen deserialization can leave Daemon null when the file
            // predates the daemon section — restore the home-relative defaults
            // so the install form starts with usable values.
            var daemon = read.Daemon ?? DaemonConfig.WithHomeDefaults();
            // Legacy migration: old preferences files had a single `audioSource`
            // string. Promote it into the tracks list so the rest of the app
            // can treat audio uniformly.
            if (daemon.AudioTracks.Count == 0 && !string.IsNullOrWhiteSpace(daemon.AudioSource))
            {
                daemon = daemon with { AudioTracks = new[] { daemon.AudioSource! } };
            }
            // Drop the legacy field once we've migrated so subsequent saves
            // don't carry stale single-track state.
            daemon = daemon with { AudioSource = null };
            // Source-gen leaves RecordingFilters null when the file predates the
            // block or carries an explicit `"recordingFilters": null` — restore
            // the record-everything defaults.
            var filters = read.RecordingFilters ?? new RecordingFilters();
            // Same null-restore for the storage block — older files predate it
            // or may carry an explicit null.
            var storage = read.StorageManagement ?? new StorageManagement();
            // Migrate the legacy `maxStorageGigabytes` key (when this number meant
            // a clip-usage cap) into the free-space floor, then drop it so the
            // next Save writes only `minFreeGigabytes`.
            if (storage.LegacyMaxStorageGigabytes is { } legacy)
            {
                storage = storage with { MinFreeGigabytes = legacy, LegacyMaxStorageGigabytes = null };
            }
            // Same null-restore for the chat-overlay block — older files predate
            // it or may carry an explicit null. Defaults leave masking off.
            var chatOverlay = read.ChatOverlay ?? new ChatOverlay();
            return read with
            {
                Daemon = daemon,
                RecordingFilters = filters,
                StorageManagement = storage,
                ChatOverlay = chatOverlay,
            };
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public void Save(UserPreferences prefs)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            using (var stream = File.Create(tmp))
            {
                JsonSerializer.Serialize(stream, prefs, UserPreferencesJsonContext.Default.UserPreferences);
            }
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}
