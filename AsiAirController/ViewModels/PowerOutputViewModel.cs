using CommunityToolkit.Mvvm.ComponentModel;
using AsiAirController.Models;

namespace AsiAirController.ViewModels;

public partial class PowerOutputViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public int Index { get; }

    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private bool   _isOn;
    [ObservableProperty] private double _voltageV;
    [ObservableProperty] private double _currentA;
    [ObservableProperty] private bool   _powerOnAtPlanStart;
    [ObservableProperty] private bool   _powerOffAtPlanEnd;

    // Cached from pi_output_get2 — required to echo back in pi_output_set2
    public string ChannelType { get; private set; } = "other";
    public bool   IsPwm       { get; private set; }
    public int    PwmValue    { get; private set; } = 100;

    public string DefaultName      => $"Output {Index + 1}";
    public string DisplayName      => string.IsNullOrWhiteSpace(Label) ? DefaultName : Label;
    public string TypeLabel        => ChannelType switch { "telescope" => "Telescope", "flat_panel" => "Flat Panel", "usb" => "USB", _ => "Aux" };
    public string VoltageText      => $"{VoltageV:F1}V";
    public string CurrentText      => $"{CurrentA:F2}A";
    public string ToggleButtonText => IsOn ? "Turn Off" : "Turn On";

    public PowerOutputViewModel(int index, AppSettings settings)
    {
        Index     = index;
        _settings = settings;

        var cfg = settings.GetOrCreateOutputConfig(index);
        _label              = cfg.Label;
        _powerOnAtPlanStart = cfg.PowerOnAtPlanStart;
        _powerOffAtPlanEnd  = cfg.PowerOffAtPlanEnd;
    }

    partial void OnLabelChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        var cfg = _settings.GetOrCreateOutputConfig(Index);
        cfg.Label = value;
        _settings.Save();
    }

    partial void OnPowerOnAtPlanStartChanged(bool value)
    {
        var cfg = _settings.GetOrCreateOutputConfig(Index);
        cfg.PowerOnAtPlanStart = value;
        _settings.Save();
    }

    partial void OnPowerOffAtPlanEndChanged(bool value)
    {
        var cfg = _settings.GetOrCreateOutputConfig(Index);
        cfg.PowerOffAtPlanEnd = value;
        _settings.Save();
    }

    partial void OnIsOnChanged(bool value) => OnPropertyChanged(nameof(ToggleButtonText));
    partial void OnVoltageVChanged(double value) => OnPropertyChanged(nameof(VoltageText));
    partial void OnCurrentAChanged(double value) => OnPropertyChanged(nameof(CurrentText));

    // Called when pi_output_get2 data arrives — updates explicit on/off state and channel metadata
    public void UpdateState(bool isOn, string type, bool isPwm, int value)
    {
        IsOn       = isOn;
        ChannelType = type;
        IsPwm      = isPwm;
        PwmValue   = value;
        OnPropertyChanged(nameof(TypeLabel));
    }

    // Called when get_power_supply data arrives — updates live voltage/current readings
    public void UpdateLive(double voltageV, double currentA)
    {
        VoltageV = voltageV;
        CurrentA = currentA;
    }
}
