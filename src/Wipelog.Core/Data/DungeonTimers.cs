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

namespace Wipelog.Core.Data;

/// <summary>
/// Mythic+ par times keyed by the WoW <c>CHALLENGE_MODE_START</c>
/// <c>challengeModeId</c> field (the second numeric column after the zone
/// name). Used to derive the +1 / +2 / +3 key upgrade level from
/// <c>ChallengeModeEnd.Duration</c>.
///
/// Conservative: the table is intentionally empty by default. The daemon
/// logs the observed challengeModeId + duration on every M+ finalize, so
/// the user (or a follow-up patch) can fill in the par time once verified
/// against the live client.  Lookups for unknown IDs return null and the
/// UI falls back to the existing Timed / Depleted label without "+N".
/// </summary>
public static class DungeonTimers
{
    /// <summary>
    /// Blizzard's M+ chest tiers are par × 1.0 (+1), × 0.8 (+2), × 0.6 (+3).
    /// </summary>
    private const double PlusTwoFraction = 0.80;
    private const double PlusThreeFraction = 0.60;

    private static readonly IReadOnlyDictionary<int, TimeSpan> ParTimes =
        new Dictionary<int, TimeSpan>
        {
            // Populated empirically from daemon logs after each M+ finalize.
            // Format: { challengeModeId, TimeSpan.FromMinutes(N).Add(TimeSpan.FromSeconds(M)) }
            //
            // Example (once verified):
            //   { 392, TimeSpan.FromMinutes(32) }, // Algeth'ar Academy
        };

    /// <summary>Par time for <paramref name="challengeModeId"/>, or null when unknown.</summary>
    public static TimeSpan? GetParTime(int challengeModeId) =>
        ParTimes.TryGetValue(challengeModeId, out var t) ? t : null;

    /// <summary>
    /// Returns the key upgrade level for an in-time finish:
    /// 1 = +1 chest (par × 1.0), 2 = +2 (par × 0.8), 3 = +3 (par × 0.6).
    /// Returns 0 when over par (depleted by time). Returns null when the
    /// dungeon's par time is unknown — caller should fall back to plain
    /// "Timed" / "Depleted" without a chest count.
    /// </summary>
    public static int? GetUpgradeLevel(int challengeModeId, TimeSpan duration)
    {
        var par = GetParTime(challengeModeId);
        return par is null ? null : ComputeUpgradeLevel(par.Value, duration);
    }

    /// <summary>
    /// Pure helper exposed for tests + diagnostics. <paramref name="duration"/>
    /// > <paramref name="par"/> → 0 (depleted). Otherwise 1/2/3 by the
    /// standard Blizzard chest tiers (par × 1.0 / 0.8 / 0.6).
    /// </summary>
    public static int ComputeUpgradeLevel(TimeSpan par, TimeSpan duration)
    {
        if (duration > par) return 0;
        if (duration <= TimeSpan.FromTicks((long)(par.Ticks * PlusThreeFraction))) return 3;
        if (duration <= TimeSpan.FromTicks((long)(par.Ticks * PlusTwoFraction))) return 2;
        return 1;
    }
}
