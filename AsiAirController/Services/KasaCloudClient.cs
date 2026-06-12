using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AsiAirController.Models;

namespace AsiAirController.Services;

public static class KasaCloudClient
{
    private static readonly HttpClient Http = new();
    private const string BaseUrl = "https://wap.tplinkcloud.com";

    /// <summary>Authenticates and returns a session token.</summary>
    public static async Task<string> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var body = $"{{\"method\":\"login\",\"params\":{{\"appType\":\"Kasa_Android\"," +
                   $"\"cloudUserName\":\"{email}\",\"cloudPassword\":\"{password}\"," +
                   $"\"terminalUUID\":\"{Guid.NewGuid()}\"}}}}";
        var json = await PostAsync(BaseUrl, body, token: null, ct);
        var node = JsonNode.Parse(json);
        if (node?["error_code"]?.GetValue<int>() != 0)
            throw new Exception($"Kasa login failed: {node?["msg"]?.GetValue<string>() ?? json}");
        return node["result"]!["token"]!.GetValue<string>();
    }

    /// <summary>
    /// Returns all controllable plugs. Strips are expanded into one entry per outlet using
    /// the outlet's own alias and child-id.
    /// </summary>
    public static async Task<IReadOnlyList<KasaDevice>> GetDevicesAsync(string token, CancellationToken ct = default)
    {
        const string body = "{\"method\":\"getDeviceList\",\"params\":{}}";
        var json = await PostAsync(BaseUrl, body, token, ct);
        var node = JsonNode.Parse(json);
        if (node?["error_code"]?.GetValue<int>() != 0)
            throw new Exception($"Kasa getDeviceList failed: {node?["msg"]?.GetValue<string>() ?? json}");

        var list = node["result"]!["deviceList"]!.AsArray();
        var rawDevices = list.Select(d => new KasaDevice(
            d!["deviceId"]!.GetValue<string>(),
            d["alias"]?.GetValue<string>() ?? d["deviceName"]?.GetValue<string>() ?? "Unknown",
            d["appServerUrl"]?.GetValue<string>() ?? BaseUrl,
            d["status"]?.GetValue<int>() == 1
        )).ToList();

        // Expand strips — each offline/standalone device falls back to itself
        var expanded = new List<KasaDevice>();
        foreach (var device in rawDevices)
            expanded.AddRange(await ExpandStripAsync(token, device, ct));
        return expanded;
    }

    /// <summary>
    /// If the device is a multi-outlet strip, returns one KasaDevice per child plug
    /// (preserving each outlet's own alias and child-id). Otherwise returns the device as-is.
    /// </summary>
    private static async Task<IReadOnlyList<KasaDevice>> ExpandStripAsync(
        string token, KasaDevice device, CancellationToken ct)
    {
        try
        {
            var innerJson   = "{\"system\":{\"get_sysinfo\":{}}}";
            var requestData = JsonSerializer.Serialize(innerJson);
            var body        = $"{{\"method\":\"passthrough\",\"params\":{{\"deviceId\":\"{device.DeviceId}\",\"requestData\":{requestData}}}}}";
            var json        = await PostAsync(device.AppServerUrl, body, token, ct);
            var node        = JsonNode.Parse(json);
            var responseData = node?["result"]?["responseData"]?.GetValue<string>();
            if (responseData == null) return [device];

            var inner    = JsonNode.Parse(responseData);
            var children = inner?["system"]?["get_sysinfo"]?["children"]?.AsArray();
            if (children == null || children.Count == 0) return [device];

            return children.Select((c, i) => new KasaDevice(
                device.DeviceId,
                c?["alias"]?.GetValue<string>() ?? $"Plug {i + 1}",
                device.AppServerUrl,
                device.IsOnline,
                c?["id"]?.GetValue<string>()
            )).ToList();
        }
        catch
        {
            return [device];
        }
    }

    /// <summary>Turns the relay on (true) or off (false). Uses child context for strip outlets.</summary>
    public static async Task SetRelayStateAsync(string token, KasaDevice device, bool on, CancellationToken ct = default)
    {
        var stateVal = on ? 1 : 0;
        var innerJson = device.ChildId != null
            ? $"{{\"context\":{{\"child_ids\":[\"{device.ChildId}\"]}},\"system\":{{\"set_relay_state\":{{\"state\":{stateVal}}}}}}}"
            : $"{{\"system\":{{\"set_relay_state\":{{\"state\":{stateVal}}}}}}}";
        var requestData = JsonSerializer.Serialize(innerJson);
        var body        = $"{{\"method\":\"passthrough\",\"params\":{{\"deviceId\":\"{device.DeviceId}\",\"requestData\":{requestData}}}}}";
        var json        = await PostAsync(device.AppServerUrl, body, token, ct);
        var node        = JsonNode.Parse(json);
        if (node?["error_code"]?.GetValue<int>() != 0)
            throw new Exception($"Kasa set_relay_state failed: {node?["msg"]?.GetValue<string>() ?? json}");
    }

    /// <summary>
    /// Returns current relay state. For strip outlets, reads the matching child's state from sysinfo.
    /// </summary>
    public static async Task<bool?> GetRelayStateAsync(string token, KasaDevice device, CancellationToken ct = default)
    {
        var innerJson   = "{\"system\":{\"get_sysinfo\":{}}}";
        var requestData = JsonSerializer.Serialize(innerJson);
        var body        = $"{{\"method\":\"passthrough\",\"params\":{{\"deviceId\":\"{device.DeviceId}\",\"requestData\":{requestData}}}}}";
        var json        = await PostAsync(device.AppServerUrl, body, token, ct);
        var node        = JsonNode.Parse(json);
        if (node?["error_code"]?.GetValue<int>() != 0) return null;

        var responseData = node["result"]?["responseData"]?.GetValue<string>();
        if (responseData == null) return null;
        var inner = JsonNode.Parse(responseData);

        if (device.ChildId != null)
        {
            var children = inner?["system"]?["get_sysinfo"]?["children"]?.AsArray();
            var child    = children?.FirstOrDefault(c => c?["id"]?.GetValue<string>() == device.ChildId);
            return child?["state"]?.GetValue<int>() == 1;
        }

        return inner?["system"]?["get_sysinfo"]?["relay_state"]?.GetValue<int>() == 1;
    }

    private static async Task<string> PostAsync(string url, string body, string? token, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var uri     = token != null ? $"{url}?token={token}" : url;
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp    = await Http.PostAsync(uri, content, cts.Token);
        return await resp.Content.ReadAsStringAsync(cts.Token);
    }
}
