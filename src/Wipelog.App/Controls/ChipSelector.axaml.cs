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

using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace Wipelog.App.Controls;

/// <summary>
/// Generic horizontal multi-select. Renders <see cref="SelectedItems"/> as
/// removable pills and offers the remaining <see cref="AvailableItems"/> through
/// an "+ Add" flyout. Removing a chip moves the item back into
/// <see cref="AvailableItems"/>; adding moves it the other way. Knows nothing
/// about the item type — reusable for any selectable set.
/// </summary>
public partial class ChipSelector : UserControl
{
    public static readonly StyledProperty<IList?> SelectedItemsProperty =
        AvaloniaProperty.Register<ChipSelector, IList?>(nameof(SelectedItems),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IList?> AvailableItemsProperty =
        AvaloniaProperty.Register<ChipSelector, IList?>(nameof(AvailableItems),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> LabelFormatProperty =
        AvaloniaProperty.Register<ChipSelector, string>(nameof(LabelFormat), "{0}");

    public IList? SelectedItems
    {
        get => GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    public IList? AvailableItems
    {
        get => GetValue(AvailableItemsProperty);
        set => SetValue(AvailableItemsProperty, value);
    }

    public string LabelFormat
    {
        get => GetValue(LabelFormatProperty);
        set => SetValue(LabelFormatProperty, value);
    }

    static ChipSelector()
    {
        SelectedItemsProperty.Changed.AddClassHandler<ChipSelector>((c, e) => c.OnItemsSourceChanged(e));
        AvailableItemsProperty.Changed.AddClassHandler<ChipSelector>((c, e) => c.OnItemsSourceChanged(e));
        LabelFormatProperty.Changed.AddClassHandler<ChipSelector>((c, _) => c.Rebuild());
    }

    public ChipSelector()
    {
        InitializeComponent();
    }

    private void OnItemsSourceChanged(AvaloniaPropertyChangedEventArgs e)
    {
        // Re-render when an external party swaps or mutates a bound collection.
        if (e.OldValue is INotifyCollectionChanged oldCol)
            oldCol.CollectionChanged -= OnBoundCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCol)
            newCol.CollectionChanged += OnBoundCollectionChanged;
        Rebuild();
    }

    private void OnBoundCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        var panel = this.FindControl<WrapPanel>("PART_Panel");
        if (panel is null) return;
        panel.Children.Clear();

        if (SelectedItems is not null)
        {
            foreach (var item in SelectedItems)
                panel.Children.Add(BuildChip(item));
        }

        if (ChipSelectorLogic.CanAdd(AvailableItems))
            panel.Children.Add(BuildAddButton());
    }

    private Control BuildChip(object item)
    {
        var label = new TextBlock { Text = FormatLabel(item) };
        label.Classes.Add("chip-label");

        var remove = new Button { Content = "✕", FontSize = 11 };
        remove.Classes.Add("chip-remove");
        remove.Click += (_, _) =>
        {
            ChipSelectorLogic.Deselect(AvailableItems, SelectedItems, item);
            Rebuild();
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(label);
        content.Children.Add(remove);

        var chip = new Border { Child = content, Margin = new Thickness(0, 0, 6, 6) };
        chip.Classes.Add("chip-item");
        return chip;
    }

    private Control BuildAddButton()
    {
        var list = new ListBox
        {
            ItemsSource = AvailableItems,
            MinWidth = 120,
            ItemTemplate = new FuncDataTemplate<object>((item, _) =>
                new TextBlock { Text = item is null ? string.Empty : FormatLabel(item) }, true),
        };

        var flyout = new Flyout { Content = list };

        var add = new Button { Content = "+ Add", Margin = new Thickness(0, 0, 6, 6), Flyout = flyout };
        add.Classes.Add("chip-add");

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is { } picked)
            {
                ChipSelectorLogic.Select(AvailableItems, SelectedItems, picked);
                list.SelectedItem = null;
                flyout.Hide();
                Rebuild();
            }
        };

        return add;
    }

    private string FormatLabel(object item) =>
        string.Format(CultureInfo.CurrentCulture, string.IsNullOrEmpty(LabelFormat) ? "{0}" : LabelFormat, item);
}

/// <summary>
/// Pure move logic for <see cref="ChipSelector"/>, factored out so it can be
/// unit-tested against plain lists without instantiating Avalonia visuals.
/// </summary>
public static class ChipSelectorLogic
{
    /// <summary>Move <paramref name="item"/> from available into selected.</summary>
    public static void Select(IList? available, IList? selected, object item)
    {
        if (selected is not null && !selected.Contains(item))
            selected.Add(item);
        available?.Remove(item);
    }

    /// <summary>Move <paramref name="item"/> from selected back into available.</summary>
    public static void Deselect(IList? available, IList? selected, object item)
    {
        if (available is not null && !available.Contains(item))
            available.Add(item);
        selected?.Remove(item);
    }

    /// <summary>True when there is at least one item to offer in the Add flyout.</summary>
    public static bool CanAdd(IList? available) => available is { Count: > 0 };
}
