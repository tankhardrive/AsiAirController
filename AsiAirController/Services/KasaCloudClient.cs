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

    /// <summary>Returns all devices on the account.</summary>
    public static async Task<IReadOnlyList<KasaDevice>> GetDevicesAsync(string token, CancellationToken ct = default)
    {
        const string body = "{\"method\":\"getDeviceList\",\"params\":{}}";
        var json = await PostAsync(BaseUrl, body, token, ct);
        var node = JsonNode.Parse(json);
        if (node?["error_code"]?.GetValue<int>() != 0)
            throw new Exception($"Kasa getDeviceList failed: {node?["msg"]?.GetValue<string>() ?? json}");
        var list = node["result"]!["deviceList"]!.AsArray();
        return list.Select(d => new KasaDevice(
            d!["deviceId"]!.GetValue<string>(),
            d["alias"]?.GetValue<string>() ?? d["deviceName"]?.GetValue<string>() ?? "Unknown",
            d["appServerUrl"]?.GetValue<string>() ?? BaseUrl,
            d["status"]?.GetValue<int>() == 1
        )).ToList();
    }

    /// <summary>Turns the relay on (true) or off (false).</summary>
    public static async Task SetRelayStateAsync(string token, KasaDevice device, bool on, CancellationToken ct = default)
    {
        var innerJson       = $"{{\"system\":{{\"set_relay_state\":{{\"state\":{(on ? 1 : 0)}}}}}}}";
        var requestData     = JsonSerializer.Serialize(innerJson);
        var body            = $"{{\"method\":\"passthrough\",\"params\":{{\"deviceId\":\"{device.DeviceId}\",\"requestData\":{requestData}}}}}";
        var json            = await PostAsync(device.AppServerUrl, body, token, ct);
        var node            = JsonNode.Parse(json);
        if (node?["error_code"]?.GetValue<int>() != 0)
            throw new Exception($"Kasa set_relay_state failed: {node?["msg"]?.GetValue<string>() ?? json}");
    }

    /// <summary>Returns current relay state, or null if unavailable.</summary>
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
