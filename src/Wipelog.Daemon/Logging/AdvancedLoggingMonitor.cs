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

using Microsoft.Extensions.Logging;
using Wipelog.Core.CombatLog;
using Wipelog.Daemon.Notifications;
using Wipelog.Daemon.Status;

namespace Wipelog.Daemon.Logging;

/// <summary>
/// Watches every <c>COMBAT_LOG_VERSION</c> line for the
/// <c>ADVANCED_LOG_ENABLED</c> flag and:
/// <list type="bullet">
///   <item>Flips <see cref="HeartbeatState.SetAdvancedLoggingDisabled"/> so
///         the persistent sidebar warning stays in sync.</item>
///   <item>Fires a one-shot desktop notification on the OFF→ON-of-disabled
///         transition (initial detection or after the user toggles the
///         setting off again mid-session).</item>
/// </list>
///
/// <para>
/// The transition gate keeps re-notifications quiet: WoW re-emits the
/// header on every <c>/reload</c>, and without the gate the user would
/// get spammed every few minutes while leveling. Toggling ACL back on
/// clears the flag and re-arms the notifier so a later toggle-off fires
/// again.
/// </para>
/// </summary>
internal sealed class AdvancedLoggingMonitor : IAdvancedLoggingObserver
{
    private const string NotifyTitle = "Wipelog: Advanced Combat Logging is OFF";
    private const string NotifyBody =
        "Recording features that depend on the advanced log block — boss HP%, kill-blow " +
        "attribution, raid composition — won't capture. Turn it on in WoW: Network " +
        "options -> Advanced Combat Logging.";

    private readonly HeartbeatState _state;
    private readonly INotificationService _notifications;
    private readonly ILogger<AdvancedLoggingMonitor> _logger;

    // null until we've seen at least one COMBAT_LOG_VERSION. After that,
    // remembers the most recent ADVANCED_LOG_ENABLED value so we only act
    // on transitions instead of every header.
    private bool? _lastSeenEnabled;

    public AdvancedLoggingMonitor(
        HeartbeatState state,
        INotificationService notifications,
        ILogger<AdvancedLoggingMonitor> logger)
    {
        _state = state;
        _notifications = notifications;
        _logger = logger;
    }

    public void OnCombatLogVersion(CombatLogVersion ev)
    {
        var prev = _lastSeenEnabled;
        if (prev == ev.AdvancedLogging) return;
        _lastSeenEnabled = ev.AdvancedLogging;
        _state.SetAdvancedLoggingDisabled(!ev.AdvancedLogging);

        if (!ev.AdvancedLogging)
        {
            _logger.LogWarning(
                "Combat log header has ADVANCED_LOG_ENABLED=0 — recordings will lack " +
                "damage attribution, boss HP%, and raid composition data.");
            // Fire-and-forget: notify-send shells out and we don't want to
            // block the event loop on it. Errors are logged by the service.
            _ = _notifications.NotifyErrorAsync(NotifyTitle, NotifyBody, CancellationToken.None);
        }
        else if (prev is not null)
        {
            _logger.LogInformation(
                "Combat log header has ADVANCED_LOG_ENABLED=1 — full data being captured.");
        }
    }
}
