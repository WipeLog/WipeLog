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

namespace Wipelog.Core.Detection;

public sealed record PlayerIdentity(
    string PlayerGuid,
    string? Name,
    string? Realm,
    string? Region,
    string? Class,
    string? Spec,
    int? Faction = null)
{
    /// <summary>
    /// Splits a combat-log source name like "Aelara-DefiasBrotherhood-EU"
    /// into (Name, Realm, Region). Same-realm players appear as bare "Name"
    /// in their own log; cross-realm group members get the realm and (for
    /// the recording player viewed from their own log) region suffix.
    ///
    /// Handles: 3-part = Name-Realm-Region; 2-part = Name-Realm, region null;
    /// 1-part = Name only; null/empty input passes through. Extra dashes are
    /// dropped (no known WoW realm name contains a dash, so this is safe).
    /// </summary>
    public static (string? Name, string? Realm, string? Region) ParseFullName(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return (fullName, null, null);
        var parts = fullName.Split('-');
        return parts.Length switch
        {
            1 => (parts[0], null, null),
            2 => (parts[0], parts[1], null),
            _ => (parts[0], parts[1], parts[2]),
        };
    }
}
