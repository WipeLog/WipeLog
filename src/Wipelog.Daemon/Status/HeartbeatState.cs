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
using Wipelog.Core.Detection;
using Wipelog.Core.Status;
using Wipelog.Core.Time;

namespace Wipelog.Daemon.Status;

/// <summary>
/// Thread-safe mutable holder for the daemon's current status. The lifecycle
/// observer mutates it on the recorder thread; <see cref="HeartbeatWriter"/>
/// reads a snapshot every 2 seconds. <see cref="Snapshot"/> stamps the latest
/// heartbeat time at call site so the read is always fresh.
/// </summary>
internal sealed class HeartbeatState
{
    private static readonly TimeSpan LastErrorRetention = TimeSpan.FromMinutes(5);

    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly string _version;
    private readonly int _pid;
    private readonly DateTimeOffset _startedAt;

    private DaemonState _state = DaemonState.Idle;
    private RecordingStatus? _currentRecording;
    private long _clipsRecorded;
    private string? _lastError;
    private DateTimeOffset? _lastErrorAt;
    private bool _advancedLoggingDisabled;
    private string? _recorderError;
    private DiskUsageInfo? _diskUsage;

    public HeartbeatState(IClock clock)
    {
        _clock = clock;
        _version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        _pid = Environment.ProcessId;
        _startedAt = _clock.Now;
    }

    public void MarkRecordingStarted(RecordingMetadata metadata)
    {
        lock (_gate)
        {
            _state = DaemonState.Recording;
            _currentRecording = new RecordingStatus(
                ContentType: metadata.Type.ToString(),
                DisplayName: metadata.DisplayName,
                StartedAt: metadata.StartedAt,
                KeystoneLevel: metadata.KeystoneLevel);
        }
    }

    public void MarkRecordingFinalized()
    {
        lock (_gate)
        {
            _state = DaemonState.Idle;
            _currentRecording = null;
            _clipsRecorded++;
        }
    }

    public void MarkError(string message)
    {
        lock (_gate)
        {
            _lastError = message;
            _lastErrorAt = _clock.Now;
        }
    }

    /// <summary>
    /// Sticky capture-failure flag — pass null when a clip finalizes with a
    /// real file so the warning clears once the recorder provably works.
    /// </summary>
    public void SetRecorderError(string? message)
    {
        lock (_gate)
        {
            _recorderError = message;
        }
    }

    public void SetAdvancedLoggingDisabled(bool disabled)
    {
        lock (_gate)
        {
            _advancedLoggingDisabled = disabled;
        }
    }

    public void SetDiskUsage(DiskUsageInfo usage)
    {
        lock (_gate)
        {
            _diskUsage = usage;
        }
    }

    public DaemonStatus Snapshot()
    {
        lock (_gate)
        {
            var now = _clock.Now;
            if (_lastErrorAt is { } at && now - at > LastErrorRetention)
            {
                _lastError = null;
                _lastErrorAt = null;
            }
            return new DaemonStatus(
                Version: _version,
                Pid: _pid,
                StartedAt: _startedAt,
                HeartbeatAt: now,
                State: _state,
                CurrentRecording: _currentRecording,
                ClipsRecordedThisSession: _clipsRecorded,
                LastError: _lastError)
            {
                AdvancedLoggingDisabled = _advancedLoggingDisabled,
                RecorderError = _recorderError,
                DiskUsage = _diskUsage,
            };
        }
    }

    public DaemonStatus FinalShutdownSnapshot()
    {
        lock (_gate)
        {
            return new DaemonStatus(
                Version: _version,
                Pid: _pid,
                StartedAt: _startedAt,
                HeartbeatAt: _clock.Now,
                State: DaemonState.Idle,
                CurrentRecording: null,
                ClipsRecordedThisSession: _clipsRecorded,
                LastError: _lastError)
            {
                AdvancedLoggingDisabled = _advancedLoggingDisabled,
                RecorderError = _recorderError,
                DiskUsage = _diskUsage,
            };
        }
    }
}
