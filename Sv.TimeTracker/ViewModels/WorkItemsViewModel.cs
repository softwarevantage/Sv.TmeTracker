using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.ViewModels;

public partial class WorkItemsViewModel : ObservableObject
{
    private readonly IWorkItemRepository _repository;
    private long? _editingId;

    public ObservableCollection<WorkItem> Items { get; } = [];

    public IReadOnlyList<string> ColorPalette { get; } =
    [
        "#0078D4",
        "#38BDF8",
        "#10B981",
        "#F59E0B",
        "#F43F5E",
        "#8B5CF6",
        "#EC4899",
        "#6366F1"
    ];

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private string _editorHeader = "New Work Item";

    [ObservableProperty]
    private string _draftTitle = string.Empty;

    [ObservableProperty]
    private string _draftDescription = string.Empty;

    [ObservableProperty]
    private string _draftColor = "#0078D4";

    [ObservableProperty]
    private string? _editorValidationError;

    [ObservableProperty]
    private WorkItem? _pendingDeleteItem;

    [ObservableProperty]
    private string? _statusMessage;

    public event Func<long?, Task>? WorkItemsMutated;
    public event Func<long, bool, Task>? ActivateWorkItemRequested;

    public WorkItemsViewModel(IWorkItemRepository repository)
    {
        _repository = repository;
    }

    public async Task LoadAsync()
    {
        var items = (await _repository.GetAllAsync()).ToList();
        UiDispatcher.Invoke(() =>
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        });
    }

    [RelayCommand]
    public void OpenNewEditor()
    {
        _editingId = null;
        EditorHeader = "New Work Item";
        DraftTitle = string.Empty;
        DraftDescription = string.Empty;
        DraftColor = ColorPalette[Items.Count % ColorPalette.Count];
        EditorValidationError = null;
        PendingDeleteItem = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    public void OpenEditEditor(WorkItem? item)
    {
        if (item == null)
        {
            return;
        }

        _editingId = item.Id;
        EditorHeader = "Edit Work Item";
        DraftTitle = item.Title;
        DraftDescription = item.Description ?? string.Empty;
        DraftColor = item.Color;
        EditorValidationError = null;
        PendingDeleteItem = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    public void SelectColorSwatch(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            DraftColor = hex;
        }
    }

    [RelayCommand]
    public void CancelEditor()
    {
        IsEditorOpen = false;
        EditorValidationError = null;
    }

    [RelayCommand]
    public async Task SaveEditorAsync()
    {
        var title = DraftTitle.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            EditorValidationError = "Work item title is required.";
            return;
        }

        var normalizedColor = NormalizeHexColor(DraftColor);
        if (normalizedColor == null)
        {
            EditorValidationError = "Enter a valid hex color (e.g. #0078D4).";
            return;
        }

        long affectedId;
        if (_editingId.HasValue)
        {
            var updated = new WorkItem
            {
                Id = _editingId.Value,
                Title = title,
                Description = string.IsNullOrWhiteSpace(DraftDescription) ? null : DraftDescription.Trim(),
                Color = normalizedColor
            };
            await _repository.UpdateAsync(updated);
            affectedId = updated.Id;
            StatusMessage = $"Updated \"{updated.Title}\".";
        }
        else
        {
            var created = await _repository.CreateAsync(new WorkItem
            {
                Title = title,
                Description = string.IsNullOrWhiteSpace(DraftDescription) ? null : DraftDescription.Trim(),
                Color = normalizedColor
            });
            affectedId = created.Id;
            StatusMessage = $"Created \"{created.Title}\".";
        }

        IsEditorOpen = false;
        EditorValidationError = null;
        await LoadAsync();

        if (WorkItemsMutated != null)
        {
            await WorkItemsMutated.Invoke(affectedId);
        }
    }

    [RelayCommand]
    public void RequestDelete(WorkItem? item)
    {
        if (item == null)
        {
            return;
        }

        IsEditorOpen = false;
        PendingDeleteItem = item;
    }

    [RelayCommand]
    public void CancelDelete()
    {
        PendingDeleteItem = null;
    }

    [RelayCommand]
    public async Task ConfirmDeleteAsync()
    {
        if (PendingDeleteItem == null)
        {
            return;
        }

        var removedTitle = PendingDeleteItem.Title;
        var removedId = PendingDeleteItem.Id;
        PendingDeleteItem = null;

        await _repository.DeleteAsync(removedId);
        StatusMessage = $"Deleted \"{removedTitle}\".";

        await LoadAsync();
        if (WorkItemsMutated != null)
        {
            await WorkItemsMutated.Invoke(null);
        }
    }

    [RelayCommand]
    public async Task SetActiveAsync(WorkItem? item)
    {
        if (item == null || ActivateWorkItemRequested == null)
        {
            return;
        }

        await ActivateWorkItemRequested.Invoke(item.Id, false);
    }

    [RelayCommand]
    public async Task StartNowAsync(WorkItem? item)
    {
        if (item == null || ActivateWorkItemRequested == null)
        {
            return;
        }

        await ActivateWorkItemRequested.Invoke(item.Id, true);
    }

    private static string? NormalizeHexColor(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "#0078D4";
        }

        var candidate = input.Trim();
        if (!candidate.StartsWith('#'))
        {
            candidate = "#" + candidate;
        }

        try
        {
            return ColorConverter.ConvertFromString(candidate) is Color
                ? candidate.ToUpperInvariant()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
