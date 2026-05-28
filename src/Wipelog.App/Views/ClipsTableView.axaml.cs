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

using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Dialogs;
using Wipelog.App.ViewModels;
using Wipelog.Library;

namespace Wipelog.App.Views;

public partial class ClipsTableView : UserControl
{
    private ClipFiltersViewModel? _filters;
    private bool _columnsBuilt;

    public ClipsTableView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private ClipListViewModel? Vm => DataContext as ClipListViewModel;
    private ClipListItemViewModel? Selected => Vm?.SelectedItem;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (App.Current is not App app) return;
        _filters = app.Services.GetRequiredService<ClipFiltersViewModel>();
        _filters.PropertyChanged += OnFiltersPropertyChanged;
        if (Vm is not null) Vm.Items.CollectionChanged += OnItemsChanged;
        var grid = this.FindControl<DataGrid>("ClipsGrid");
        if (grid is not null) grid.SelectionChanged += OnGridSelectionChanged;
        EnsureColumnsBuilt();
        ApplyColumnVisibility(_filters.ContentType);
        UpdateEmptyState();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_filters is not null) _filters.PropertyChanged -= OnFiltersPropertyChanged;
        if (Vm is not null) Vm.Items.CollectionChanged -= OnItemsChanged;
        var grid = this.FindControl<DataGrid>("ClipsGrid");
        if (grid is not null) grid.SelectionChanged -= OnGridSelectionChanged;
    }

    /// <summary>
    /// Mirrors the DataGrid's native multi-selection into
    /// <see cref="ClipListViewModel.SelectedItems"/>. Avalonia's
    /// <c>DataGrid.SelectedItems</c> isn't two-way bindable, so the canonical
    /// workaround is a code-behind handler that pushes adds/removes through.
    /// </summary>
    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is null) return;
        foreach (var removed in e.RemovedItems)
        {
            if (removed is ClipListItemViewModel item) Vm.SelectedItems.Remove(item);
        }
        foreach (var added in e.AddedItems)
        {
            if (added is ClipListItemViewModel item && !Vm.SelectedItems.Contains(item))
            {
                Vm.SelectedItems.Add(item);
            }
        }
    }

    private void OnFiltersPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipFiltersViewModel.ContentType) && _filters is not null)
        {
            ApplyColumnVisibility(_filters.ContentType);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateEmptyState();

    private void UpdateEmptyState()
    {
        if (this.FindControl<StackPanel>("EmptyState") is { } panel)
        {
            panel.IsVisible = Vm is not null && Vm.Items.Count == 0;
        }
    }

    /// <summary>
    /// Build all variant-spanning columns once at Load. Re-creating columns on
    /// each rail change (Columns.Clear()+Add) leaves Avalonia's DataGrid with
    /// stale Auto/Star arbitration: the new chip columns render truncated
    /// ("Tin" instead of "Timed"). Keeping the column set fixed and toggling
    /// IsVisible per variant sidesteps the measurement bug entirely. The
    /// columns are added in a fixed order (Content, Dungeon, Encounter, Title,
    /// Key, Difficulty, Result, POV, Duration, Date) chosen so that hiding
    /// the variant-exclusive ones leaves the expected display order intact for
    /// every variant.
    /// </summary>
    private void EnsureColumnsBuilt()
    {
        if (_columnsBuilt) return;
        var grid = this.FindControl<DataGrid>("ClipsGrid");
        if (grid is null) return;
        grid.Columns.Clear();
        foreach (var col in BuildAllColumns()) grid.Columns.Add(col);
        _columnsBuilt = true;
    }

    private void ApplyColumnVisibility(ContentTypeFilter filter)
    {
        var grid = this.FindControl<DataGrid>("ClipsGrid");
        if (grid is null) return;
        var visible = VisibleColumnHeaders(ClipsTableColumnSet.For(filter));
        foreach (var col in grid.Columns)
        {
            if (col.Header is string header) col.IsVisible = visible.Contains(header);
        }
    }

    private static HashSet<string> VisibleColumnHeaders(ClipsTableVariant v) => v switch
    {
        ClipsTableVariant.MythicPlus => new() { "Dungeon",   "Key",        "Result", "POV", "Duration", "Date" },
        ClipsTableVariant.Raid       => new() { "Encounter", "Difficulty", "Result", "POV", "Duration", "Date" },
        _                            => new() { "Content",   "Title",      "Result", "POV", "Duration", "Date" },
    };

    private IEnumerable<DataGridColumn> BuildAllColumns() => new[]
    {
        Column("Content",    "auto", "ContentBadgeCell", "Record.ContentType",     minWidth: 100),
        Column("Dungeon",    "2*",   "DungeonCell",      "Record.DungeonName",     minWidth: 160),
        Column("Encounter",  "2*",   "EncounterCell",    "Record.DisplayName",     minWidth: 160),
        Column("Title",      "2*",   "AllTitleCell",     "Record.DisplayName",     minWidth: 160),
        Column("Key",        "auto", "KeystoneCell",     "Record.KeystoneLevel",   minWidth: 64),
        Column("Difficulty", "auto", "DifficultyCell",   "Record.Difficulty",      minWidth: 80),
        Column("Result",     "auto", "ResultCell",       "Record.Success",         minWidth: 80),
        Column("POV",        "1.2*", "PovCell",          "Record.PlayerName",      minWidth: 140),
        Column("Duration",   "auto", "DurationCell",     "Record.DurationSeconds", minWidth: 72),
        Column("Date",       "auto", "DateCell",         "Record.StartedAt",       minWidth: 96),
    };

    private DataGridColumn Column(string header, string width, string templateKey, string sortPath, double? minWidth = null)
    {
        var tmpl = (IDataTemplate)Resources[templateKey]!;
        var col = new DataGridTemplateColumn
        {
            Header = header,
            Width = ParseWidth(width),
            CellTemplate = tmpl,
            SortMemberPath = sortPath,
        };
        if (minWidth is double m) col.MinWidth = m;
        return col;
    }

    private static DataGridLength ParseWidth(string spec)
    {
        // "auto" → size-to-content; "80" → 80 px fixed; "2*" / "1.2*" → star sizing.
        if (string.Equals(spec, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return new DataGridLength(0, DataGridLengthUnitType.Auto);
        }
        if (spec.EndsWith('*'))
        {
            var v = spec.Length == 1
                ? 1.0
                : double.Parse(spec[..^1], System.Globalization.CultureInfo.InvariantCulture);
            return new DataGridLength(v, DataGridLengthUnitType.Star);
        }
        return new DataGridLength(
            double.Parse(spec, System.Globalization.CultureInfo.InvariantCulture));
    }

    private void OnClearFiltersClick(object? sender, RoutedEventArgs e)
    {
        _filters?.ClearFiltersCommand.Execute(null);
    }

    private void OnContextPlay(object? sender, RoutedEventArgs e)
    {
        if (Selected is null || App.Current is not App app) return;
        var player = app.Services.GetRequiredService<PlayerViewModel>();
        player.LoadClip(Selected.Record.VideoPath);
    }

    private async void OnContextCopyPath(object? sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        var top = TopLevel.GetTopLevel(this);
        var clipboard = top?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(Selected.Record.VideoPath);
        }
    }

    private void OnContextDelete(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        _ = Vm.DeleteSingleCommand.ExecuteAsync(null);
    }

    private void OnContextMask(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        _ = Vm.CreateMaskedCopyCommand.ExecuteAsync(null);
    }

    private void OnContextEditMetadata(object? sender, RoutedEventArgs e)
    {
        if (Selected is null || App.Current is not App app) return;

        var record = Selected.Record;
        var vm = new EditClipMetadataDialogViewModel(record);
        var content = new EditClipMetadataContent { DataContext = vm };
        var dialogs = app.Services.GetRequiredService<ISukiDialogManager>();

        dialogs.CreateDialog()
            .WithTitle("Edit clip metadata")
            .WithContent(content)
            .WithActionButton("Save", dlg => { _ = ApplyMetadataAsync(app, record, vm.BuildPatch()); }, dismissOnClick: true)
            .WithActionButton("Cancel", dlg => { }, dismissOnClick: true)
            .TryShow();
    }

    private static async Task ApplyMetadataAsync(App app, ClipRecord record, ClipMetadataPatch patch)
    {
        var repo = app.Services.GetRequiredService<IClipRepository>();
        await repo.UpdateClipMetadataAsync(record.Id, patch, default);
        var list = app.Services.GetRequiredService<ClipListViewModel>();
        await list.InitializeAsync();
    }
}
