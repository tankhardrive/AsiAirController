namespace AsiAirController.Models;

// ChildId is null for standalone plugs; set to the plug's child-id string for strips.
public record KasaDevice(string DeviceId, string Alias, string AppServerUrl, bool IsOnline, string? ChildId = null);
