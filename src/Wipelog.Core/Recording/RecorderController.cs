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
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Data;
using Wipelog.Core.Detection;
using Wipelog.Core.Preferences;

namespace Wipelog.Core.Recording;

/// <summary>
/// Optional hook the daemon uses to insert a row into the library after a
/// clip is finalized. Decoupled from the SQLite types so Core stays
/// dependency-free; the daemon project supplies the implementation.
/// </summary>
public interface IClipRecorderSink
{
    Task RecordAsync(ClipFinalization clip, CancellationToken ct);
}

public sealed record ClipFinalization(
    string VideoPath,
    string SidecarPath,
    string? EventsSidecarPath,
    ContentType Type,
    string DisplayName,
    int? DifficultyId,
    int? EncounterId,
    int? KeystoneLevel,
    bool Success,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationSeconds,
    long FileSizeBytes,
    string? PlayerName,
    string? PlayerRealm,
    string? PlayerRegion,
    string? PlayerClass,
    string? PlayerSpec,
    string? InstanceName,
    string? DungeonName,
    string? ArenaBracket = null,
    // Remaining boss HP percent (0..100) at wipe, raid only. Null for kills,
    // for non-raid content, and for raids where we never matched the boss
    // (multi-boss encounters, advanced logging off).
    double? BossHpPercent = null,
    // M+ key upgrade level: 1/2/3 chest for an in-time finish, 0 depleted,
    // null non-M+ or unknown-par dungeon. Mirrors ClipSidecar.KeyUpgradeLevel.
    int? KeyUpgradeLevel = null);

/// <summary>
/// Wires <see cref="EncounterDetector"/> output to an <see cref="IRecorder"/>:
/// pulls events from a channel, runs them through the detector, and turns the
/// resulting <see cref="StartRecording"/> / <see cref="StopRecording"/>
/// commands into recorder calls + JSON sidecar files.
/// </summary>
public sealed class RecorderController : IAsyncDisposable
{
    private const string TempPrefix = ".";
    private const string TempSuffix = ".recording.mp4";
    private const string SidecarExtension = ".json";
    private const string EventsSidecarExtension = ".events.json";
    private const int SidecarVersion = 2;

    // Identity-resolution buffer is bounded so a long recording doesn't
    // accumulate millions of events; COMBATANT_INFO + first source-named
    // events both arrive within the first few seconds.
    private const int IdentityBufferCap = 500;

    private static readonly TimeSpan KillingBlowLookback = TimeSpan.FromSeconds(5);

    private readonly EncounterDetector _detector;
    private readonly IRecorder _recorder;
    private readonly IClipNamingStrategy _naming;
    private readonly string _outputDirectory;
    private readonly string? _playerName;
    private readonly PlayerSpecResolver _resolver;
    private readonly ILogger<RecorderController> _logger;
    private readonly IClipRecorderSink? _sink;
    private readonly IRecordingLifecycleObserver? _observer;
    private readonly IPreRollRecorder? _preRoll;
    private readonly IAdvancedLoggingObserver? _aclObserver;
    private readonly IReadOnlyList<string>? _audioTrackTitles;
    private readonly IRecordingStartGate? _startGate;
    private readonly int _postRollSeconds;
    private readonly ChatOverlay? _chatOverlay;
    private readonly string? _captureExecutable;
    private readonly string? _captureTarget;
    // Cached source monitor height for the chat-overlay scale calculation.
    // Resolved lazily on the first overlay apply (the daemon's startup path
    // doesn't query monitors). Stays in source-monitor pixels; the scale is
    // recomputed per clip from the actual video height.
    private int? _sourceMonitorHeight;

    private StartRecording? _activeStart;
    private string? _activeTempPath;
    private string? _activePreRollPath;

    private List<CombatLogEvent>? _identityBuffer;
    private RecordingEventBuffer? _eventBuffer;

    // Serializes the event loop's mutations of recording state with any
    // out-of-band abort triggered by the control socket. The lock is only
    // contended when a user actively presses Stop in the GUI — the event
    // loop is single-writer otherwise so cost is one uncontested semaphore
    // acquire per combat-log event.
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // Captures the recorder's unexpected exit so the event loop can finalize
    // the current clip as a failure on its next tick. Read/written without a
    // lock — single-writer (recorder thread) / single-reader (event loop) and
    // a few millisecond delay is fine.
    private RecorderExitedUnexpectedlyEventArgs? _pendingUnexpectedExit;

    public RecorderController(
        EncounterDetector detector,
        IRecorder recorder,
        IClipNamingStrategy naming,
        string outputDirectory,
        ILogger<RecorderController> logger,
        IClipRecorderSink? sink = null,
        string? playerName = null,
        ILogger<PlayerSpecResolver>? resolverLogger = null,
        IRecordingLifecycleObserver? observer = null,
        IPreRollRecorder? preRoll = null,
        IAdvancedLoggingObserver? aclObserver = null,
        IReadOnlyList<string>? audioSources = null,
        IRecordingStartGate? startGate = null,
        int postRollSeconds = 0,
        ChatOverlay? chatOverlay = null,
        string? captureExecutable = null,
        string? captureTarget = null)
    {
        _detector = detector;
        _recorder = recorder;
        _naming = naming;
        _outputDirectory = outputDirectory;
        _logger = logger;
        _sink = sink;
        _observer = observer;
        _preRoll = preRoll;
        _aclObserver = aclObserver;
        _startGate = startGate;
        _playerName = playerName;
        _audioTrackTitles = audioSources is { Count: > 0 }
            ? AudioTrackNaming.FriendlyTitles(audioSources)
            : null;
        _postRollSeconds = Math.Max(0, postRollSeconds);
        _chatOverlay = chatOverlay;
        _captureExecutable = captureExecutable;
        _captureTarget = captureTarget;
        _resolver = new PlayerSpecResolver(
            resolverLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PlayerSpecResolver>.Instance);
        _recorder.ExitedUnexpectedly += OnRecorderExitedUnexpectedly;
    }

    private void OnRecorderExitedUnexpectedly(object? sender, RecorderExitedUnexpectedlyEventArgs args)
    {
        // Latch the most recent unexpected exit; the event loop picks it up
        // on its next iteration and finalizes the active clip as a failure.
        // Recording typically has constant combat-log traffic so the next
        // iteration arrives within seconds.
        _pendingUnexpectedExit = args;
    }

    public async Task RunAsync(ChannelReader<CombatLogEvent> events, CancellationToken ct)
    {
        Directory.CreateDirectory(_outputDirectory);
        if (_preRoll is not null)
        {
            try
            {
                await _preRoll.StartAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to start pre-roll recorder; clips will record without pre-roll.");
            }
        }
        try
        {
            await foreach (var evt in events.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await HandlePendingUnexpectedExitAsync(ct).ConfigureAwait(false);

                    foreach (var cmd in _detector.Process(evt))
                    {
                        await HandleCommandAsync(cmd, ct).ConfigureAwait(false);
                    }
                    BufferForIdentity(evt);
                    _eventBuffer?.Observe(evt);

                    // Forward COMBAT_LOG_VERSION lines to the ACL monitor so
                    // the daemon can warn the user when Advanced Combat
                    // Logging is off. The observer is expected to be cheap;
                    // any heavy work it triggers is its own concern.
                    if (evt is CombatLogVersion clv && _aclObserver is not null)
                    {
                        try { _aclObserver.OnCombatLogVersion(clv); }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Advanced-logging observer threw.");
                        }
                    }
                }
                finally
                {
                    _stateLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // Take the same lock as AbortCurrentAsync so a control-socket
            // abort racing with shutdown can't double-finalize the clip.
            await _stateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_activeStart is not null)
                {
                    _logger.LogInformation("Shutting down with an active recording — finalizing as failure.");
                    await SafeStopAsync(success: false, ct: CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _stateLock.Release();
            }
            if (_preRoll is not null)
            {
                try
                {
                    await _preRoll.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Pre-roll recorder threw during shutdown.");
                }
            }
        }
    }

    private async Task HandlePendingUnexpectedExitAsync(CancellationToken ct)
    {
        var info = Interlocked.Exchange(ref _pendingUnexpectedExit, null);
        if (info is null) return;
        if (_activeStart is null) return;

        var name = _activeStart.Metadata.DisplayName;
        var stderr = string.IsNullOrEmpty(info.StderrTail) ? "(no stderr captured)" : info.StderrTail;
        var message =
            $"Recorder process died mid-recording (exitCode={info.ExitCode}) for {name}. " +
            $"Clip will be finalized as failure. Last stderr:\n{stderr}";
        _logger.LogError("{Message}", message);

        await NotifyErrorAsync(message, ex: null, ct).ConfigureAwait(false);
        await SafeStopAsync(success: false, ct).ConfigureAwait(false);
    }

    private void BufferForIdentity(CombatLogEvent evt)
    {
        if (_identityBuffer is null) return;
        if (_identityBuffer.Count >= IdentityBufferCap) return;
        _identityBuffer.Add(evt);
    }

    private PlayerIdentity? ResolveIdentity()
    {
        if (_identityBuffer is null || _identityBuffer.Count == 0) return null;
        try
        {
            return _resolver.Resolve(_identityBuffer, _playerName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Resolver threw — continuing without identity.");
            return null;
        }
    }

    private async Task HandleCommandAsync(RecordingCommand cmd, CancellationToken ct)
    {
        switch (cmd)
        {
            case StartRecording start:
                await OnStartAsync(start, ct).ConfigureAwait(false);
                break;
            case StopRecording stop:
                await OnStopAsync(stop, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task OnStartAsync(StartRecording start, CancellationToken ct)
    {
        if (_activeStart is not null)
        {
            _logger.LogWarning(
                "StartRecording received while another recording is active ({Active}); stopping it first.",
                _activeStart.SuggestedFilename);
            await SafeStopAsync(success: false, ct).ConfigureAwait(false);
        }

        var tempName = $"{TempPrefix}{start.SuggestedFilename}{TempSuffix}";
        var tempPath = Path.Combine(_outputDirectory, tempName);

        _logger.LogInformation(
            "Starting recording for {Name} → {TempPath}",
            start.Metadata.DisplayName,
            tempPath);

        // Free disk space before writing a new clip. Best-effort: a throw here
        // must never block the recording, so swallow everything except
        // cancellation. The new temp file isn't a DB row yet, so cleanup can't
        // evict the clip we're about to record.
        if (_startGate is not null)
        {
            try
            {
                await _startGate.EnsureCapacityAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Disk capacity check threw; recording anyway.");
            }
        }

        try
        {
            await _recorder.StartAsync(tempPath, ct).ConfigureAwait(false);
            _activeStart = start;
            _activeTempPath = tempPath;
            _activePreRollPath = null;
            _identityBuffer = new List<CombatLogEvent>(IdentityBufferCap);
            // Pass the encounter's display name through so the buffer can
            // tag damage events on the boss for the "Wiped at X%" readout.
            // Only meaningful for raid encounters; the buffer ignores it for
            // any content type whose damage events never name the boss.
            _eventBuffer = new RecordingEventBuffer(
                start.Metadata.StartedAt, KillingBlowLookback,
                bossName: start.Metadata.Type == ContentType.RaidEncounter
                    ? start.Metadata.DisplayName
                    : null);

            // Flush the pre-roll buffer while the main recorder warms up.
            // SaveAsync is a no-op when no pre-roll recorder is configured.
            if (_preRoll is not null)
            {
                try
                {
                    _activePreRollPath = await _preRoll.SaveAsync(ct).ConfigureAwait(false);
                    if (_activePreRollPath is not null)
                    {
                        _logger.LogInformation(
                            "Captured pre-roll for {Name} → {Path}",
                            start.Metadata.DisplayName, _activePreRollPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Pre-roll save threw for {Name}; clip will record without pre-roll.",
                        start.Metadata.DisplayName);
                }
            }
            await NotifyStartedAsync(start.Metadata, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recorder for {Name}", start.Metadata.DisplayName);
            _activeStart = null;
            _activeTempPath = null;
            _activePreRollPath = null;
            _identityBuffer = null;
            _eventBuffer = null;
            await NotifyErrorAsync($"Failed to start recorder for {start.Metadata.DisplayName}", ex, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task NotifyStartedAsync(RecordingMetadata metadata, CancellationToken ct)
    {
        if (_observer is null) return;
        try
        {
            await _observer.OnRecordingStartedAsync(metadata, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lifecycle observer threw during OnRecordingStarted.");
        }
    }

    private async Task NotifyFinalizedAsync(ClipFinalization clip, CancellationToken ct)
    {
        if (_observer is null) return;
        try
        {
            await _observer.OnRecordingFinalizedAsync(clip, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lifecycle observer threw during OnRecordingFinalized.");
        }
    }

    private async Task NotifyErrorAsync(string message, Exception? ex, CancellationToken ct)
    {
        if (_observer is null) return;
        try
        {
            await _observer.OnRecordingErrorAsync(message, ex, ct).ConfigureAwait(false);
        }
        catch (Exception inner)
        {
            _logger.LogDebug(inner, "Lifecycle observer threw during OnRecordingError.");
        }
    }

    private async Task OnStopAsync(StopRecording stop, CancellationToken ct)
    {
        if (_activeStart is null || _activeTempPath is null)
        {
            _logger.LogWarning("StopRecording received but no recording is active; ignoring.");
            return;
        }

        // Post-roll: hold the recorder open for N more seconds so the saved
        // clip includes the immediate aftermath (kill cam, wipe panic, key
        // chest opening). Skipped on shutdown — Task.Delay throws when ct is
        // cancelled and we drop straight into the stop path. Manual stops
        // go through SafeStopAsync and never touch this code.
        var postRollApplied = 0;
        if (_postRollSeconds > 0)
        {
            try
            {
                _logger.LogInformation(
                    "Holding {Name} open for {Seconds}s post-roll.",
                    _activeStart.Metadata.DisplayName, _postRollSeconds);
                await Task.Delay(TimeSpan.FromSeconds(_postRollSeconds), ct).ConfigureAwait(false);
                postRollApplied = _postRollSeconds;
            }
            catch (OperationCanceledException)
            {
                // Shutdown — stop immediately without finishing the tail.
            }
        }

        try
        {
            await _recorder.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recorder threw while stopping; will attempt to finalize anyway.");
        }

        await FinalizeAsync(stop, ct, postRollApplied).ConfigureAwait(false);
    }

    private async Task FinalizeAsync(StopRecording stop, CancellationToken ct, int postRollSeconds = 0)
    {
        var start = _activeStart!;
        var tempPath = _activeTempPath!;
        var preRollPath = _activePreRollPath;
        var identity = ResolveIdentity();
        var eventBuffer = _eventBuffer;

        // Arena clips arrive with Success=true + an "_complete" placeholder
        // suffix from the detector (faction context isn't known there). Now
        // that we have both the player's faction and the match's winning
        // faction, finalise the outcome by rewriting Success + filename.
        stop = ResolveArenaOutcome(stop, identity);

        _activeStart = null;
        _activeTempPath = null;
        _activePreRollPath = null;
        _identityBuffer = null;
        _eventBuffer = null;

        if (!File.Exists(tempPath))
        {
            var message =
                $"Recording for {start.Metadata.DisplayName} finished but temp file " +
                $"{tempPath} does not exist — nothing to finalize. " +
                "The recorder process likely died mid-recording.";
            _logger.LogWarning("{Message}", message);
            await NotifyErrorAsync(message, ex: null, ct).ConfigureAwait(false);
            TryDeletePreRoll(preRollPath);
            return;
        }

        var finalPath = _naming.Resolve(_outputDirectory, stop.SuggestedFilename);
        try
        {
            File.Move(tempPath, finalPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename {From} → {To}", tempPath, finalPath);
            TryDeletePreRoll(preRollPath);
            return;
        }

        // Prepend the pre-roll snippet so the saved clip includes the seconds
        // before ENCOUNTER_START. Returns the actual prepended duration on
        // success, null on failure (clip retains no pre-roll).
        double? preRollSeconds = await TryPrependPreRollAsync(preRollPath, finalPath, ct)
            .ConfigureAwait(false);

        // Composite the chat-region mask over the (now complete, pre-roll
        // included) video before it is indexed, so the library only ever sees
        // the masked clip. Best-effort: a failure keeps the original.
        await TryApplyChatOverlayAsync(finalPath, ct).ConfigureAwait(false);

        // Boss HP at wipe: raid wipes only. The buffer tracked HP for any
        // damage events on the encounter's named boss; we only surface it
        // when the encounter actually wiped. Kills and non-raid content
        // leave the field null.
        double? bossHpPercent = null;
        if (start.Metadata.Type == ContentType.RaidEncounter && !stop.Success)
        {
            bossHpPercent = eventBuffer?.GetLastBossHpPercent();
        }

        // M+ chest level was computed in the detector if the dungeon's par
        // is known to DungeonTimers. Log the (challengeModeId, duration)
        // pair on every M+ finalize so the user can populate the table
        // empirically for dungeons we don't ship par times for.
        if (start.Metadata.Type == ContentType.MythicPlus)
        {
            var runDuration = stop.EndedAt - start.Metadata.StartedAt;
            _logger.LogInformation(
                "M+ finalize: dungeon={Dungeon} challengeModeId={Id} duration={Duration:c} " +
                "success={Success} keyUpgradeLevel={Level}",
                start.Metadata.DisplayName,
                start.Metadata.ChallengeModeId,
                runDuration,
                stop.Success,
                stop.KeyUpgradeLevel?.ToString() ?? "(unknown par)");
        }

        var (instanceName, dungeonName) = ResolveInstanceNames(start.Metadata);

        // Surface the post-roll seconds on the sidecars so the UI's scrub bar
        // knows the video extends past EncounterEnd. Zero collapses to null
        // (matches PreRoll's "absent for clips recorded without it" rule).
        double? postRollForSidecar = postRollSeconds > 0 ? postRollSeconds : null;
        await WriteSidecarAsync(
                finalPath, start, stop, identity, instanceName, dungeonName,
                preRollSeconds, postRollForSidecar, bossHpPercent, ct)
            .ConfigureAwait(false);
        var eventsSidecarPath = await WriteEventsSidecarAsync(
                finalPath, start, stop, eventBuffer, preRollSeconds, postRollForSidecar, ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Saved recording → {Path}", finalPath);

        var startedAt = start.Metadata.StartedAt;
        var endedAt = stop.EndedAt < startedAt ? startedAt : stop.EndedAt;
        var size = File.Exists(finalPath) ? new FileInfo(finalPath).Length : 0;
        var sidecarPath = Path.ChangeExtension(finalPath, ".json");
        var finalization = new ClipFinalization(
            VideoPath: finalPath,
            SidecarPath: sidecarPath,
            EventsSidecarPath: eventsSidecarPath,
            Type: start.Metadata.Type,
            DisplayName: start.Metadata.DisplayName,
            DifficultyId: start.Metadata.DifficultyId,
            EncounterId: start.Metadata.EncounterId,
            KeystoneLevel: start.Metadata.KeystoneLevel,
            Success: stop.Success,
            StartedAt: startedAt,
            EndedAt: endedAt,
            DurationSeconds: (int)Math.Max(0, (endedAt - startedAt).TotalSeconds),
            FileSizeBytes: size,
            PlayerName: identity?.Name,
            PlayerRealm: identity?.Realm,
            PlayerRegion: identity?.Region,
            PlayerClass: identity?.Class,
            PlayerSpec: identity?.Spec,
            InstanceName: instanceName,
            DungeonName: dungeonName,
            ArenaBracket: start.Metadata.ArenaBracket,
            BossHpPercent: bossHpPercent,
            KeyUpgradeLevel: stop.KeyUpgradeLevel);

        if (_sink is not null)
        {
            try
            {
                await _sink.RecordAsync(finalization, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to record clip in library — indexer will recover next start.");
            }
        }

        await NotifyFinalizedAsync(finalization, ct).ConfigureAwait(false);
    }

    private async Task<string?> WriteEventsSidecarAsync(
        string finalVideoPath,
        StartRecording start,
        StopRecording stop,
        RecordingEventBuffer? buffer,
        double? preRollSeconds,
        double? postRollSeconds,
        CancellationToken ct)
    {
        if (buffer is null) return null;
        var path = Path.ChangeExtension(finalVideoPath, null) + EventsSidecarExtension;
        try
        {
            var endedAt = stop.EndedAt < start.Metadata.StartedAt ? start.Metadata.StartedAt : stop.EndedAt;
            var doc = buffer.Build(endedAt, preRollSeconds, postRollSeconds);
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(fs, doc, TimelineEventsJson.Options, ct).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to write events sidecar at {Path}; clip is finalized without it.", path);
            return null;
        }
    }

    /// <summary>
    /// For Arena clips where both ArenaMatchEnd.WinningFaction and the
    /// recording player's faction are known, rewrites the detector's
    /// placeholder Success=true + "_complete" filename into the real
    /// outcome ("_win" or "_loss"). All other paths return the input
    /// unchanged so M+, Raid, Solo Shuffle and BG keep their existing
    /// behaviour until separate signals come along.
    /// </summary>
    private static StopRecording ResolveArenaOutcome(StopRecording stop, PlayerIdentity? identity)
    {
        if (stop.WinningFaction is not { } wf) return stop;
        if (identity?.Faction is not { } pf) return stop;

        const string Placeholder = "_complete.mp4";
        if (!stop.SuggestedFilename.EndsWith(Placeholder, StringComparison.Ordinal))
        {
            // Detector contract drift — update success but leave the name alone.
            return stop with { Success = wf == pf };
        }
        var won = wf == pf;
        var stem = stop.SuggestedFilename[..^Placeholder.Length];
        var newFilename = $"{stem}_{(won ? "win" : "loss")}.mp4";
        return stop with { Success = won, SuggestedFilename = newFilename };
    }

    private static (string? Instance, string? Dungeon) ResolveInstanceNames(RecordingMetadata m) =>
        m.Type switch
        {
            ContentType.MythicPlus => (null, m.DisplayName),
            ContentType.RaidEncounter => (
                m.InstanceId is { } id ? RaidInstances.GetRaidName(id) : null,
                null),
            _ => (null, null),
        };

    private async Task SafeStopAsync(bool success, CancellationToken ct)
    {
        if (_activeStart is null) return;

        try
        {
            await _recorder.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recorder threw during safe-stop.");
        }

        if (_activeTempPath is null)
        {
            _activeStart = null;
            return;
        }

        // Synthesize a stop command using the start metadata so the file can be
        // finalized with a sensible name even when no real StopRecording arrived.
        var synth = SynthesizeStop(_activeStart!, success);
        await FinalizeAsync(synth, ct).ConfigureAwait(false);
    }

    private static StopRecording SynthesizeStop(StartRecording start, bool success)
    {
        var suffix = start.Metadata.Type switch
        {
            ContentType.RaidEncounter => success ? "kill" : "wipe",
            ContentType.MythicPlus => success ? "timed" : "depleted",
            _ => success ? "ok" : "fail",
        };
        var name = $"{start.SuggestedFilename}_{suffix}.mp4";
        return new StopRecording(success, start.Metadata, name, DateTimeOffset.UtcNow);
    }

    private async Task WriteSidecarAsync(
        string finalVideoPath,
        StartRecording start,
        StopRecording stop,
        PlayerIdentity? identity,
        string? instanceName,
        string? dungeonName,
        double? preRollSeconds,
        double? postRollSeconds,
        double? bossHpPercent,
        CancellationToken ct)
    {
        var sidecarPath = Path.ChangeExtension(finalVideoPath, SidecarExtension);
        var startedAt = start.Metadata.StartedAt;
        var endedAt = stop.EndedAt < startedAt ? startedAt : stop.EndedAt;
        var duration = (int)Math.Max(0, (endedAt - startedAt).TotalSeconds);

        var sidecar = new ClipSidecar(
            Version: SidecarVersion,
            ContentType: start.Metadata.Type.ToString(),
            DisplayName: start.Metadata.DisplayName,
            Difficulty: start.Metadata.Type == ContentType.RaidEncounter
                ? DifficultyMap.DisplayName(start.Metadata.DifficultyId)
                : null,
            EncounterId: start.Metadata.EncounterId,
            KeystoneLevel: start.Metadata.KeystoneLevel,
            Success: stop.Success,
            StartedAt: startedAt.ToUniversalTime(),
            EndedAt: endedAt.ToUniversalTime(),
            DurationSeconds: duration,
            VideoFile: Path.GetFileName(finalVideoPath),
            PlayerName: identity?.Name,
            PlayerRealm: identity?.Realm,
            PlayerRegion: identity?.Region,
            PlayerClass: identity?.Class,
            PlayerSpec: identity?.Spec,
            InstanceName: instanceName,
            DungeonName: dungeonName,
            ArenaBracket: start.Metadata.ArenaBracket,
            PreRollSeconds: preRollSeconds,
            PostRollSeconds: postRollSeconds,
            BossHpPercent: bossHpPercent,
            KeyUpgradeLevel: stop.KeyUpgradeLevel);

        await using var fs = new FileStream(
            sidecarPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(fs, sidecar, ClipSidecarJson.Options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Stream-copies <paramref name="preRollPath"/> + <paramref name="finalPath"/>
    /// into a single file at <paramref name="finalPath"/> via ffmpeg. Returns
    /// the actual pre-roll duration (probed from the file) on success, null
    /// otherwise. Always deletes the pre-roll source file when done, even on
    /// failure, so it doesn't accumulate in the pre-roll directory.
    /// </summary>
    private async Task<double?> TryPrependPreRollAsync(
        string? preRollPath, string finalPath, CancellationToken ct)
    {
        if (preRollPath is null) return null;
        if (!File.Exists(preRollPath))
        {
            _logger.LogWarning("Pre-roll path {Path} disappeared before finalize; skipping concat.", preRollPath);
            return null;
        }

        double? duration = await FfmpegProbe.GetDurationSecondsAsync(preRollPath, _logger, ct)
            .ConfigureAwait(false);

        var concatTemp = finalPath + ".concat.mp4";
        var concatOk = await FfmpegConcat.ConcatAsync(
            new[] { preRollPath, finalPath }, concatTemp, _logger, ct,
            audioTrackTitles: _audioTrackTitles).ConfigureAwait(false);

        TryDeletePreRoll(preRollPath);

        if (!concatOk)
        {
            TryDeleteFile(concatTemp);
            _logger.LogWarning(
                "Concat failed; clip {Final} keeps its original (no pre-roll) form.", finalPath);
            return null;
        }
        try
        {
            File.Move(concatTemp, finalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to replace {Final} with concat output {Tmp}; keeping original.",
                finalPath, concatTemp);
            TryDeleteFile(concatTemp);
            return null;
        }
        _logger.LogInformation(
            "Prepended {Duration:0.##}s of pre-roll onto {Final}.",
            duration ?? 0, finalPath);
        return duration;
    }

    private async Task TryApplyChatOverlayAsync(string finalPath, CancellationToken ct)
    {
        if (_chatOverlay is null || !_chatOverlay.Enabled) return;

        // The mask was configured in source-monitor pixels via the GUI. When
        // the daemon downscales the recording, X/Y/W/H need to scale by
        // (video_height / source_monitor_height) so the box still covers the
        // same chat area. scale=1.0 keeps native-resolution recordings byte-
        // identical to the pre-downscale era.
        var scale = await ResolveChatOverlayScaleAsync(finalPath, ct).ConfigureAwait(false);
        var masked = await ChatOverlayProcessor.ApplyAsync(finalPath, _chatOverlay, _logger, ct, scale: scale)
            .ConfigureAwait(false);
        if (!masked)
        {
            await NotifyErrorAsync(
                    $"Chat overlay processing failed for {Path.GetFileName(finalPath)}. " +
                    "The original recording is preserved.",
                    ex: null, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task<double> ResolveChatOverlayScaleAsync(string clipPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_captureExecutable)) return 1.0;
        var videoHeight = await FfmpegProbe.GetVideoHeightAsync(clipPath, _logger, ct).ConfigureAwait(false);
        if (videoHeight is not int vh || vh <= 0) return 1.0;
        var sourceHeight = await EnsureSourceMonitorHeightAsync(ct).ConfigureAwait(false);
        if (sourceHeight is not int sh || sh <= 0) return 1.0;
        if (vh == sh) return 1.0;
        return (double)vh / sh;
    }

    private async Task<int?> EnsureSourceMonitorHeightAsync(CancellationToken ct)
    {
        if (_sourceMonitorHeight is not null) return _sourceMonitorHeight;
        if (string.IsNullOrWhiteSpace(_captureExecutable)) return null;
        try
        {
            var monitors = await GpuScreenRecorderMonitorList
                .QueryAsync(_captureExecutable, ct).ConfigureAwait(false);
            var focused = string.Equals(_captureTarget, "focused", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(_captureTarget);
            var pick = focused
                ? GpuScreenRecorderMonitorList.PickLargest(monitors)
                : (monitors.FirstOrDefault(m =>
                       string.Equals(m.Name, _captureTarget, StringComparison.Ordinal))
                   ?? GpuScreenRecorderMonitorList.PickLargest(monitors));
            _sourceMonitorHeight = pick?.Height;
            return _sourceMonitorHeight;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Could not resolve source monitor height for chat-overlay scale; using scale=1.0.");
            return null;
        }
    }

    private void TryDeletePreRoll(string? path)
    {
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete pre-roll snippet {Path}; will be cleaned up later.", path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Stops the currently active recording, if any. Success is derived from
    /// the recording type: a Manual recording (user pressed Stop on purpose)
    /// finalises with <c>Success=true</c>; any other type (encounter/dungeon
    /// the user is abandoning mid-pull) finalises with <c>Success=false</c>.
    /// Safe to call from any thread — the state lock serialises with the
    /// event loop. Returns <c>true</c> if a recording was actually stopped,
    /// <c>false</c> when the daemon was idle.
    /// </summary>
    public async Task<bool> StopCurrentAsync(CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_activeStart is null) return false;
            var success = _activeStart.Metadata.Type == ContentType.Manual;
            _logger.LogInformation(
                "Stop requested for {Type} recording {Name} (success={Success}).",
                _activeStart.Metadata.Type, _activeStart.Metadata.DisplayName, success);
            await SafeStopAsync(success: success, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Starts a free-form recording requested by the GUI (no encounter trigger).
    /// Returns <c>false</c> if a recording is already in progress — the caller
    /// should refuse rather than overwriting it. Otherwise synthesises a
    /// <see cref="StartRecording"/> command tagged as <see cref="ContentType.Manual"/>
    /// and runs it through the same lifecycle as encounter-triggered clips
    /// (pre-roll prepend, identity buffer, lifecycle observer).
    /// </summary>
    public async Task<bool> StartManualAsync(DateTimeOffset startedAt, CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_activeStart is not null) return false;
            var stem = BuildManualStem(startedAt);
            var displayName = "Manual recording - " +
                startedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture);
            var metadata = new RecordingMetadata(
                Type: ContentType.Manual,
                DisplayName: displayName,
                DifficultyId: null,
                KeystoneLevel: null,
                EncounterId: null,
                InstanceId: null,
                StartedAt: startedAt);
            var start = new StartRecording(stem, metadata);
            await OnStartAsync(start, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private static string BuildManualStem(DateTimeOffset startedAt) =>
        startedAt.ToString("yyyy-MM-dd_HH-mm-ss",
            System.Globalization.CultureInfo.InvariantCulture) + "_manual";

    public async ValueTask DisposeAsync()
    {
        _recorder.ExitedUnexpectedly -= OnRecorderExitedUnexpectedly;
        if (_activeStart is not null)
        {
            await SafeStopAsync(success: false, ct: CancellationToken.None).ConfigureAwait(false);
        }
        _stateLock.Dispose();
    }
}
