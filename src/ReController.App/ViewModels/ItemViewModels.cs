using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ReController.Core.Input;
using ReController.Core.Mapping;
using ReController.Core.Profiles;
using ReController.Core.Text;
using ReController.Platform.Windows.Input;

namespace ReController.App.ViewModels;

/// <summary>State of one control as drawn on the controller diagram.</summary>
public sealed partial class ControlBindingViewModel(Control control) : ObservableObject
{
    public Control Control { get; } = control;
    public string ShortName { get; } = Names.Short(control);
    public string LongName { get; } = Names.Long(control);

    [ObservableProperty] private string _label = Names.Short(control);
    [ObservableProperty] private bool _isRemapped;
    [ObservableProperty] private bool _isDisabled;
    [ObservableProperty] private bool _isInherited;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isLivePressed;
    [ObservableProperty] private bool _isOutputPressed;
}

/// <summary>Combined label for a multi-control group (stick, D-pad).</summary>
public sealed partial class GroupLabelViewModel : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isRemapped;

    // Present so the shared diagram label style binds cleanly; groups are never inherited/disabled as a whole.
    public bool IsInherited => false;
    public bool IsDisabled => false;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isLivePressed;
}

public sealed partial class ConfigTabViewModel(MappingConfig model) : ObservableObject
{
    public MappingConfig Model { get; } = model;

    [ObservableProperty] private string _name = model.Name;
    [ObservableProperty] private bool _isApplied;

    partial void OnNameChanged(string value) => Model.Name = value;
}

public sealed partial class GameItemViewModel : ObservableObject
{
    public GameItemViewModel(GameProfile model)
    {
        Model = model;
        _name = model.Name;
        Configs = new ObservableCollection<ConfigTabViewModel>(model.Configs.Select(c => new ConfigTabViewModel(c)));
    }

    public GameProfile Model { get; }
    public ObservableCollection<ConfigTabViewModel> Configs { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isApplied;

    public string ExecutablesSummary => Model.Executables.Count == 0 ? "No executables linked" : string.Join(", ", Model.Executables);

    partial void OnNameChanged(string value) => Model.Name = value;

    public void RefreshExecutables() => OnPropertyChanged(nameof(ExecutablesSummary));
}

public sealed partial class DeviceItemViewModel(PhysicalDeviceInfo info) : ObservableObject
{
    public PhysicalDeviceInfo Info { get; private set; } = info;
    public string Key => Info.Key;

    [ObservableProperty] private string _displayName = info.Name;
    [ObservableProperty] private int _activeSlot;
    [ObservableProperty] private bool _virtualActive;
    [ObservableProperty] private int _virtualSlot = -1;

    public string Glyph => Info.Family switch
    {
        DeviceFamily.PlayStation => "",
        DeviceFamily.Nintendo => "",
        _ => "",
    };

    public string FamilyName => Info.Family switch
    {
        DeviceFamily.Xbox => "Xbox / XInput",
        DeviceFamily.PlayStation => "PlayStation",
        DeviceFamily.Nintendo => "Nintendo",
        _ => "Generic",
    };

    public string StatusText => VirtualActive
        ? $"Slot {ActiveSlot + 1} · virtual pad #{VirtualSlot + 1}"
        : $"{FamilyName} · Slot {ActiveSlot + 1}";

    partial void OnActiveSlotChanged(int value) => OnPropertyChanged(nameof(StatusText));
    partial void OnVirtualActiveChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnVirtualSlotChanged(int value) => OnPropertyChanged(nameof(StatusText));

    public void Update(PhysicalDeviceInfo info, string? alias)
    {
        Info = info;
        DisplayName = string.IsNullOrWhiteSpace(alias) ? info.Name : alias;
    }
}

public sealed partial class SlotItemViewModel(int index) : ObservableObject
{
    public int Index { get; } = index;
    public string Title => $"Slot {Index + 1}";

    [ObservableProperty] private string _assigned = "Empty";
    [ObservableProperty] private bool _isActive;
}

public sealed partial class IndicatorViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private bool _isOn;
}
