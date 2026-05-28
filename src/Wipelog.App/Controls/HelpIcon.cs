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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wipelog.App.Controls;

/// <summary>
/// A small inline "ⓘ" affordance carrying an explanatory tooltip, placed next
/// to a field label. Usage: <c>&lt;controls:HelpIcon Tip="…"/&gt;</c>.
///
/// Drawn as a bordered circle around a lowercase "i" rather than the Unicode
/// ⓘ glyph (absent from the app's UI font — it rendered as tofu). Colors and
/// hover behavior are set in code so the control doesn't depend on an external
/// style sheet being in scope.
/// </summary>
public sealed class HelpIcon : Border
{
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#80848e"));
    private static readonly IBrush Hover = new SolidColorBrush(Color.Parse("#b5bac1"));

    public static readonly StyledProperty<string?> TipProperty =
        AvaloniaProperty.Register<HelpIcon, string?>(nameof(Tip));

    private readonly TextBlock _glyph;

    public string? Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    public HelpIcon()
    {
        Width = 15;
        Height = 15;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        BorderBrush = Muted;
        // A transparent (not null) background makes the whole circle hit-testable
        // so the tooltip fires anywhere over the icon, not just on the glyph.
        Background = Brushes.Transparent;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(5, 0, 0, 0);
        Cursor = new Cursor(StandardCursorType.Help);
        _glyph = new TextBlock
        {
            Text = "i",
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _glyph;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        BorderBrush = Hover;
        _glyph.Foreground = Hover;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        BorderBrush = Muted;
        _glyph.Foreground = Muted;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TipProperty)
        {
            ToolTip.SetTip(this, Tip);
        }
    }
}
