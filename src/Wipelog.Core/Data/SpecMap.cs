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
/// Maps WoW spec IDs (as written by COMBATANT_INFO) to (Class, Spec, Role)
/// triples. Covers current retail (12.x): 13 classes x 3-4 specs.
///
/// Role is Blizzard's canonical role designation for the spec, used for
/// raid-composition badges. Augmentation Evoker is classified as Dps even
/// though it's a support spec, matching the in-game LFG role.
/// </summary>
public static class SpecMap
{
    public static readonly IReadOnlyDictionary<int, (string Class, string Spec, SpecRole Role)> Known =
        new Dictionary<int, (string, string, SpecRole)>
        {
            // Death Knight
            { 250, ("Death Knight", "Blood",  SpecRole.Tank) },
            { 251, ("Death Knight", "Frost",  SpecRole.Dps) },
            { 252, ("Death Knight", "Unholy", SpecRole.Dps) },
            // Demon Hunter
            { 577, ("Demon Hunter", "Havoc",     SpecRole.Dps) },
            { 581, ("Demon Hunter", "Vengeance", SpecRole.Tank) },
            { 1480, ("Demon Hunter", "Devourer", SpecRole.Dps) },
            // Druid
            { 102, ("Druid", "Balance",     SpecRole.Dps) },
            { 103, ("Druid", "Feral",       SpecRole.Dps) },
            { 104, ("Druid", "Guardian",    SpecRole.Tank) },
            { 105, ("Druid", "Restoration", SpecRole.Healer) },
            // Evoker
            { 1467, ("Evoker", "Devastation",  SpecRole.Dps) },
            { 1468, ("Evoker", "Preservation", SpecRole.Healer) },
            // Aug is officially Dps in the LFG role picker despite being support.
            { 1473, ("Evoker", "Augmentation", SpecRole.Dps) },
            // Hunter
            { 253, ("Hunter", "Beast Mastery", SpecRole.Dps) },
            { 254, ("Hunter", "Marksmanship",  SpecRole.Dps) },
            { 255, ("Hunter", "Survival",      SpecRole.Dps) },
            // Mage
            { 62,  ("Mage", "Arcane", SpecRole.Dps) },
            { 63,  ("Mage", "Fire",   SpecRole.Dps) },
            { 64,  ("Mage", "Frost",  SpecRole.Dps) },
            // Monk
            { 268, ("Monk", "Brewmaster", SpecRole.Tank) },
            { 269, ("Monk", "Windwalker", SpecRole.Dps) },
            { 270, ("Monk", "Mistweaver", SpecRole.Healer) },
            // Paladin
            { 65,  ("Paladin", "Holy",        SpecRole.Healer) },
            { 66,  ("Paladin", "Protection",  SpecRole.Tank) },
            { 70,  ("Paladin", "Retribution", SpecRole.Dps) },
            // Priest
            { 256, ("Priest", "Discipline", SpecRole.Healer) },
            { 257, ("Priest", "Holy",       SpecRole.Healer) },
            { 258, ("Priest", "Shadow",     SpecRole.Dps) },
            // Rogue
            { 259, ("Rogue", "Assassination", SpecRole.Dps) },
            { 260, ("Rogue", "Outlaw",        SpecRole.Dps) },
            { 261, ("Rogue", "Subtlety",      SpecRole.Dps) },
            // Shaman
            { 262, ("Shaman", "Elemental",   SpecRole.Dps) },
            { 263, ("Shaman", "Enhancement", SpecRole.Dps) },
            { 264, ("Shaman", "Restoration", SpecRole.Healer) },
            // Warlock
            { 265, ("Warlock", "Affliction",  SpecRole.Dps) },
            { 266, ("Warlock", "Demonology",  SpecRole.Dps) },
            { 267, ("Warlock", "Destruction", SpecRole.Dps) },
            // Warrior
            { 71,  ("Warrior", "Arms",       SpecRole.Dps) },
            { 72,  ("Warrior", "Fury",       SpecRole.Dps) },
            { 73,  ("Warrior", "Protection", SpecRole.Tank) },
        };

    public static (string Class, string Spec, SpecRole Role)? Lookup(int specId) =>
        Known.TryGetValue(specId, out var v) ? v : null;
}

public enum SpecRole
{
    Tank,
    Healer,
    Dps,
}
