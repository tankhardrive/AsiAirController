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

    public string DefaultName      => $"Output {Index + 1}";
    public string DisplayName      => string.IsNullOrWhiteSpace(Label) ? DefaultName : Label;
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

    partial void OnVoltageVChanged(double value)
    {
        OnPropertyChanged(nameof(VoltageText));
        // Infer on/off from voltage — TODO: revisit once SET command confirmed
        IsOn = value > 1.0;
    }

    partial void OnCurrentAChanged(double value) => OnPropertyChanged(nameof(CurrentText));

    public void UpdateLive(double voltageV, double currentA)
    {
        VoltageV = voltageV;
        CurrentA = currentA;
    }
}
