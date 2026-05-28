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
/// Maps (Class, Spec) pairs to the basename of the spec icon shipped under
/// Wipelog.App/Assets/Icons/Specs/{name}.jpg. Keys mirror the strings produced
/// by <see cref="SpecMap"/>, so the resolver's output can be passed straight
/// in. Returns null for unknown pairs (e.g. Demon Hunter "Devourer" — present
/// in SpecMap but no upstream icon yet); callers should fall back to a
/// class-color swatch in that case.
///
/// Source data: tmp/wow_spec_icons.json, scraped from wow.zamimg.com (Wowhead
/// CDN) at 56x56. The 39 covered entries match retail 12.x.
/// </summary>
public static class SpecIcons
{
    public static readonly IReadOnlyDictionary<(string Class, string Spec), string> Known =
        new Dictionary<(string, string), string>
        {
            // Death Knight
            { ("Death Knight", "Blood"),  "spell_deathknight_bloodpresence" },
            { ("Death Knight", "Frost"),  "spell_deathknight_frostpresence" },
            { ("Death Knight", "Unholy"), "spell_deathknight_unholypresence" },
            // Demon Hunter
            { ("Demon Hunter", "Havoc"),     "ability_demonhunter_specdps" },
            { ("Demon Hunter", "Vengeance"), "ability_demonhunter_spectank" },
            // Druid
            { ("Druid", "Balance"),     "spell_nature_starfall" },
            { ("Druid", "Feral"),       "ability_druid_catform" },
            { ("Druid", "Guardian"),    "ability_racial_bearform" },
            { ("Druid", "Restoration"), "spell_nature_healingtouch" },
            // Evoker
            { ("Evoker", "Devastation"),  "classicon_evoker_devastation" },
            { ("Evoker", "Preservation"), "classicon_evoker_preservation" },
            { ("Evoker", "Augmentation"), "classicon_evoker_augmentation" },
            // Hunter
            { ("Hunter", "Beast Mastery"), "ability_hunter_bestialdiscipline" },
            { ("Hunter", "Marksmanship"),  "ability_hunter_focusedaim" },
            { ("Hunter", "Survival"),      "ability_hunter_camouflage" },
            // Mage
            { ("Mage", "Arcane"), "spell_holy_magicalsentry" },
            { ("Mage", "Fire"),   "spell_fire_firebolt02" },
            { ("Mage", "Frost"),  "spell_frost_frostbolt02" },
            // Monk
            { ("Monk", "Brewmaster"), "spell_monk_brewmaster_spec" },
            { ("Monk", "Mistweaver"), "spell_monk_mistweaver_spec" },
            { ("Monk", "Windwalker"), "spell_monk_windwalker_spec" },
            // Paladin
            { ("Paladin", "Holy"),        "spell_holy_holybolt" },
            { ("Paladin", "Protection"),  "ability_paladin_shieldofthetemplar" },
            { ("Paladin", "Retribution"), "spell_holy_auraoflight" },
            // Priest
            { ("Priest", "Discipline"), "spell_holy_powerwordshield" },
            { ("Priest", "Holy"),       "spell_holy_guardianspirit" },
            { ("Priest", "Shadow"),     "spell_shadow_shadowwordpain" },
            // Rogue
            { ("Rogue", "Assassination"), "ability_rogue_deadlybrew" },
            { ("Rogue", "Outlaw"),        "ability_rogue_waylay" },
            { ("Rogue", "Subtlety"),      "ability_stealth" },
            // Shaman
            { ("Shaman", "Elemental"),   "spell_nature_lightning" },
            { ("Shaman", "Enhancement"), "spell_shaman_improvedstormstrike" },
            { ("Shaman", "Restoration"), "spell_nature_magicimmunity" },
            // Warlock
            { ("Warlock", "Affliction"),  "spell_shadow_deathcoil" },
            { ("Warlock", "Demonology"),  "spell_shadow_metamorphosis" },
            { ("Warlock", "Destruction"), "spell_shadow_rainoffire" },
            // Warrior
            { ("Warrior", "Arms"),       "ability_warrior_savageblow" },
            { ("Warrior", "Fury"),       "ability_warrior_innerrage" },
            { ("Warrior", "Protection"), "ability_warrior_defensivestance" },
        };

    /// <summary>
    /// Icon basename for the given class+spec, or null if either is missing
    /// or the pair has no upstream icon. Callers append ".jpg" and resolve
    /// against the App's Assets/Icons/Specs/ folder.
    /// </summary>
    public static string? Lookup(string? playerClass, string? playerSpec) =>
        playerClass is not null && playerSpec is not null
        && Known.TryGetValue((playerClass, playerSpec), out var icon)
            ? icon
            : null;
}
