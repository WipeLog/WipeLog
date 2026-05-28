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
using Avalonia.Data.Converters;

namespace Wipelog.App.ViewModels;

public sealed class EnumEqualityConverter : IValueConverter
{
    public static readonly EnumEqualityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter : Avalonia.Data.BindingOperations.DoNothing;
}

public sealed class NullableIntConverter : IValueConverter
{
    public static readonly NullableIntConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? (decimal?)i : (decimal?)null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? (int?)(int)d : (int?)null;
}

/// <summary>True iff the bound int equals zero — used to show an "empty
/// hint" message when a collection has no items.</summary>
public sealed class IntZeroConverter : IValueConverter
{
    public static readonly IntZeroConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i == 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>
/// Two-way map between a <see cref="DateTimeOffset"/>? (the VM shape) and a
/// <see cref="DateTime"/>? (what <c>CalendarDatePicker.SelectedDate</c>
/// expects). Going VM → UI drops the offset (the picker only cares about
/// the local calendar date); UI → VM stamps the local time-zone offset so
/// the BuildQuery side can round-trip through ISO 8601.
/// </summary>
public sealed class DateTimeOffsetToDateTimeConverter : IValueConverter
{
    public static readonly DateTimeOffsetToDateTimeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset dto ? dto.DateTime : (DateTime?)null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime dt
            ? new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt))
            : (DateTimeOffset?)null;
}
