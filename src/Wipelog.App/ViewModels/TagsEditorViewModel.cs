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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wipelog.Library;

namespace Wipelog.App.ViewModels;

public sealed partial class TagsEditorViewModel : ViewModelBase
{
    private readonly IClipRepository _repo;
    private long _clipId;

    public ObservableCollection<string> Tags { get; } = new();
    public ObservableCollection<string> AvailableTags { get; } = new();

    [ObservableProperty]
    private string _draftTag = string.Empty;

    [ObservableProperty]
    private string? _validationError;

    public event EventHandler? TagsChanged;

    public TagsEditorViewModel(IClipRepository repo)
    {
        _repo = repo;
    }

    public async Task LoadAsync(long clipId)
    {
        _clipId = clipId;
        Tags.Clear();
        AvailableTags.Clear();
        DraftTag = string.Empty;
        ValidationError = null;
        if (clipId == 0) return;

        var existing = await _repo.GetTagsForClipAsync(clipId, CancellationToken.None);
        foreach (var t in existing) Tags.Add(t);
        var all = await _repo.GetAllTagsAsync(CancellationToken.None);
        foreach (var t in all) AvailableTags.Add(t);
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (!TagNormalization.TryNormalize(DraftTag, out var normalized))
        {
            ValidationError = "Lowercase letters, digits, space, dash, underscore only. Max 30 chars.";
            return;
        }
        if (Tags.Contains(normalized))
        {
            ValidationError = "Already added.";
            return;
        }
        Tags.Add(normalized);
        DraftTag = string.Empty;
        ValidationError = null;
        await PersistAsync();
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string tag)
    {
        if (Tags.Remove(tag))
        {
            await PersistAsync();
        }
    }

    private async Task PersistAsync()
    {
        if (_clipId == 0) return;
        await _repo.SetTagsForClipAsync(_clipId, Tags.ToList(), CancellationToken.None);
        TagsChanged?.Invoke(this, EventArgs.Empty);
    }
}
