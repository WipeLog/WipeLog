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
/// Maps known retail-raid instance IDs (the value WoW writes as the
/// <c>InstanceId</c> field of ENCOUNTER_START) to the raid's display name.
/// Conservative: only includes recent tiers I'm confident about — extend
/// over time. Returns null for unknown IDs (caller falls back to a
/// per-clip default).
/// </summary>
public static class RaidInstances
{
    private static readonly IReadOnlyDictionary<int, string> ById =
        new Dictionary<int, string>
        {
            { 2657, "Liberation of Undermine" },
            { 2549, "Nerub-ar Palace" },
            { 2569, "Aberrus, the Shadowed Crucible" },
            { 2522, "Vault of the Incarnates" },
            { 2481, "Sepulcher of the First Ones" },
            { 2450, "Sanctum of Domination" },
            { 2296, "Castle Nathria" },
        };

    public static string? GetRaidName(int instanceId) =>
        ById.TryGetValue(instanceId, out var name) ? name : null;
}
