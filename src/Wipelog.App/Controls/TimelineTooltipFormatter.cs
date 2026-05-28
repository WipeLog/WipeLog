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
using Wipelog.Core.Recording;

namespace Wipelog.App.Controls;

/// <summary>
/// Builds the multi-line tooltip text shown when the user hovers a timeline
/// marker. Pure helper so the three "killed by" branches and the boss-start
/// shape can be tested without spinning up Avalonia.
/// </summary>
public static class TimelineTooltipFormatter
{
    public static string Format(
        TimelineEvent ev,
        DateTimeOffset recordingStartedAt) =>
        ev switch
        {
            PlayerDeathEvent d => FormatDeath(d, recordingStartedAt),
            BossStartEvent b => FormatBossStart(b, recordingStartedAt),
            _ => string.Empty,
        };

    private static string FormatDeath(PlayerDeathEvent d, DateTimeOffset recordingStartedAt)
    {
        var time = recordingStartedAt + TimeSpan.FromMilliseconds(d.OffsetMs);
        var line1 = time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var line2 = d.PlayerName;
        var line3 = (d.KilledBySource, d.KilledBySpell) switch
        {
            ({ Length: > 0 } src, { Length: > 0 } spell) => $"Killed by {src} → {spell}",
            ({ Length: > 0 } src, _) => $"Killed by {src}",
            _ => "Cause unknown",
        };
        return $"{line1}\n{line2}\n{line3}";
    }

    private static string FormatBossStart(BossStartEvent b, DateTimeOffset recordingStartedAt)
    {
        var time = recordingStartedAt + TimeSpan.FromMilliseconds(b.OffsetMs);
        var line1 = time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"{line1}\n{b.EncounterName}";
    }
}
