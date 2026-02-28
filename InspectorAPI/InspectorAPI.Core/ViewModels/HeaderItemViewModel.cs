using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace InspectorAPI.Core.ViewModels;

public partial class HeaderItemViewModel : ViewModelBase
{
    private readonly Action<HeaderItemViewModel>? _removeAction;

    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;

    public HeaderItemViewModel() { }

    public HeaderItemViewModel(Action<HeaderItemViewModel> removeAction)
    {
        _removeAction = removeAction;
    }

    [RelayCommand]
    private void Remove() => _removeAction?.Invoke(this);
}
