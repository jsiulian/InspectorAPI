using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace InspectorAPI.Core.ViewModels;

public partial class FormPartViewModel : ViewModelBase
{
    private readonly Action<FormPartViewModel>? _removeAction;

    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private string _partContentType = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;

    public FormPartViewModel() { }

    public FormPartViewModel(Action<FormPartViewModel> removeAction)
    {
        _removeAction = removeAction;
    }

    [RelayCommand]
    private void Remove() => _removeAction?.Invoke(this);
}
