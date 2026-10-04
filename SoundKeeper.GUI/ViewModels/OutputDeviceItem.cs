using System.ComponentModel;
using Microsoft.UI.Xaml;
using SoundKeeper.GUI.Services;

namespace SoundKeeper.GUI.ViewModels;

// One row of the custom output selection: only the Windows name is shown, the endpoint ID stays internal.
public sealed class OutputDeviceItem(OutputDeviceRow row, string name, string unavailableText, Action<OutputDeviceItem> selectionChanged)
    : INotifyPropertyChanged
{
    private bool _isSelected = row.IsSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; } = row.Id;
    public string Name { get; } = name;
    public string StatusText { get; } = row.IsAvailable ? string.Empty : unavailableText;
    public Visibility StatusVisibility { get; } = row.IsAvailable ? Visibility.Collapsed : Visibility.Visible;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            selectionChanged(this);
        }
    }
}
