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

namespace Wipelog.Core.Time;

public interface IClock
{
    DateTimeOffset Now { get; }

    /// <summary>
    /// Time zone the log file's local timestamps are in. Used by the parser
    /// to compute the *historical* UTC offset for each event (so November
    /// timestamps get November's offset, not whatever offset is in effect
    /// when the parse happens — important across DST boundaries).
    /// </summary>
    TimeZoneInfo Zone { get; }
}
