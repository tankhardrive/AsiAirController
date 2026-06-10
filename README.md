# AsiAirController

A macOS desktop app for remotely controlling a [ZWO ASI Air](https://www.zwoastro.com/product/asiair/) astrophotography device at a remote observatory over a WireGuard VPN. The primary safety use case is automatically stopping imaging and parking the mount when the observatory roof closes.

---

## Features

- **Stop Exposure** — immediately halts the current camera exposure
- **Park Mount** — sends the mount to its park position
- **Safe Shutdown** — stops exposure then parks the mount in sequence
- **Roof Polling** — reads a network-share roof status file once per minute and automatically triggers a safe shutdown if the roof is not `OPEN`; displays a live countdown to the next check
- **Test** — queries `scope_get_info` and shows the raw response, useful for verifying connectivity
- IP address and roof file path persist between launches (`~/.config/AsiAirController/settings.json`)

---

## How It Works

### Command Transport

The ASI Air exposes a JSON-RPC API over TCP. Commands are sent by opening a fresh TCP connection, writing a newline-terminated JSON payload, and immediately closing both sides of the socket via `SocketShutdown.Both`.

```csharp
// Equivalent to: echo '{"id":1,"method":"scope_park"}' | nc host 4400
var payload = Encoding.UTF8.GetBytes("{\"id\": 1, \"method\": \"scope_park\"}\n");
await tcp.GetStream().WriteAsync(payload);
tcp.Client.Shutdown(SocketShutdown.Both);
```

There is no persistent connection — each button click opens its own short-lived socket. This mirrors how `nc` works and avoids connection management complexity.

**Why close immediately?**  
Port 4700 pushes a `{"Event":"Version",...}` message the moment a client connects. Any approach that holds the socket open and waits to read a response ends up consuming that Version event instead of the command response. Shutting down both sides of the socket right after writing sidesteps this entirely — the device processes the command on receipt regardless of whether the client reads a response.

### Roof Status Polling

The observatory provides a plain-text roof status file on an SMB share. The app reads it using `FileShare.ReadWrite | FileShare.Delete` so no lock is ever held on the file — required by the observatory's file access policy.

File format:
```
2026-06-09 05:16:34AM CST Roof Status: CLOSED
```

The app parses the word after `Roof Status:`. If it's anything other than `OPEN`, a safe shutdown sequence fires automatically (stop exposure → park mount). The shutdown triggers once per closed event; polling resets when the roof reopens.

**Mounting the SMB share (macOS):**  
Finder → Go → Connect to Server → `smb://172.16.5.21/sfro-customer` (username: `guest`, no password). Once mounted the file is at `/Volumes/sfro-customer/roof/building-5/RoofStatusFile.txt`.

---

## Build & Run

Requires .NET SDK 9.x or later. The project sets `<RollForward>Major</RollForward>` so it also runs on a .NET 10 runtime if that's what's installed.

```bash
dotnet build
dotnet run --project AsiAirController/AsiAirController.csproj
```

**Note for Rider users:** Avalonia source generators don't run via `dotnet build`, so `MainWindow.axaml.cs` has an explicit constructor calling `InitializeComponent()`. This is intentional and required.

---

## Tech Stack

| | |
|-|--|
| Language | C# / .NET 9 |
| UI | [Avalonia 11](https://avaloniaui.net/) with Fluent dark theme |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) |
| IDE | JetBrains Rider |

---

## ASI Air API Reference

All communication is plain-text, newline-delimited JSON over TCP. The device pushes unsolicited events interleaved with responses on the same socket.

### Request Format

```json
{"id": 1, "method": "method_name", "params": [...]}
```

`params` is omitted when not needed. `id` is used to match responses to requests.

### Response Format

```json
{"jsonrpc": "2.0", "method": "method_name", "result": 0, "code": 0, "id": 1}
```

`result: 0` and `code: 0` indicate success.

### Unsolicited Event Format

```json
{"Event": "EventName", "Timestamp": "...", ...}
```

Events are pushed at any time on the same socket, interleaved with responses.

---

## Port Map

| Port | Service |
|------|---------|
| 4400 | Mount / scope control |
| 4700 | Camera / imaging control |
| 4500 | Unknown |
| 4800 | Unknown |
| 4801 | Unknown |
| 4900 | Unknown |

---

## Port 4400 — Mount / Scope

### `scope_get_info`
Returns full mount state. Poll at ~1Hz for live status.
```json
{"id": 1, "method": "scope_get_info"}
```

Key fields in `result`:

| Field | Type | Description |
|-------|------|-------------|
| `RA` / `Dec` | double | Current coordinates (decimal hours / degrees) |
| `Az` / `Alt` | double | Horizon coordinates |
| `Lat` / `Lon` | double | Observatory location |
| `sidereal_time` | double | |
| `park_status` | string | `"parked"` or `"unparked"` |
| `is_parking` | bool | True while slewing to park |
| `move_status` | string | `"none"` when idle |
| `pier_side` | string | `"east"` or `"west"` |
| `is_enable_track` | bool | Whether tracking is active |
| `track_mode_index` | int | Index into `track_mode_list` |
| `input_voltage` | double | 12V rail in millivolts (e.g. `12282` = 12.28V) |
| `model` | string | e.g. `"ZWO AM5"` |
| `fw_ver` | string | Mount firmware version |
| `sn` | string | Mount serial number |
| `caps` | array | Capability strings: `"park"`, `"goto"`, `"sync"`, `"ctrl_track"`, `"guide_rate"`, etc. |

---

### `scope_park`
Slews the mount to its park/home position. Streams `ScopeHome` events during the slew.
```json
{"id": 1, "method": "scope_park"}
```
Response:
```json
{"jsonrpc": "2.0", "method": "scope_park", "result": 0, "code": 0, "id": 1}
```

---

### `test_connection`
```json
{"id": 1, "method": "test_connection"}
```

---

### Unsolicited Events — Port 4400

#### `ScopeHome`
Streamed during `scope_park`. Contains live RA/Dec as the mount slews.
```json
{"Event": "ScopeHome", "state": "working", "lapse_ms": 548, "RA": 15.58, "Dec": 89.71}
```
Final event when complete:
```json
{"Event": "ScopeHome", "state": "complete", "lapse_ms": 4310, "RA": 3.54, "Dec": 90.0}
```

---

## Port 4700 — Camera / Imaging

### `stop_exposure`
Immediately stops the current exposure. No params required.
```json
{"id": 1, "method": "stop_exposure"}
```

---

### `start_exposure`
```json
{"id": 1, "method": "start_exposure", "params": ["light"]}
```

---

### `get_app_state`
Returns full current state of the imaging session.
```json
{"id": 1, "method": "get_app_state"}
```
Key fields:

| Field | Description |
|-------|-------------|
| `page` | Current UI page (e.g. `"plan"`) |
| `capture.is_working` | Whether imaging is active |
| `capture.state` | e.g. `"idle"` |
| `capture.error` | Last error string (e.g. `"aborted"`) |
| `plan.is_plan_started` | Whether a plan is running |
| `plan.plan_name` | Active plan name |

---

### `get_enabled_plan`
Returns the current imaging plan with all targets and sequences.
```json
{"id": 1, "method": "get_enabled_plan"}
```
Key fields:

| Field | Description |
|-------|-------------|
| `plan_name` | e.g. `"Eagle Nebula"` |
| `start_time` / `end_time` | e.g. `{"type": "dusk"}` / `{"type": "dawn"}` |
| `targets[].target_name` | |
| `targets[].seqs[].type` | e.g. `"light"` |
| `targets[].seqs[].exp` | Exposure time in seconds |
| `targets[].seqs[].repeat` | Frame count |

---

### `get_dawn_dusk_time`
Returns today's dawn and dusk times as decimal hours.
```json
{"id": 1, "method": "get_dawn_dusk_time"}
```
Response:
```json
{"result": {"dawn": 5.831866, "dusk": 23.345425}}
```

---

### `set_plan`
```json
{"id": 1, "method": "set_plan", "params": [{"id": 2, "targets": [{"id": 1, "enable": 1}]}]}
```

### `import_plan`
```json
{"id": 1, "method": "import_plan", "params": [{"id": 2, "wait_cooling": false}]}
```

### `set_page`
```json
{"id": 1, "method": "set_page", "params": ["plan"]}
```

### `get_power_supply`
Returns voltage/current readings for all power rails. Result is an array of `[voltage, current]` pairs.

### `get_camera_bin`
Returns current binning setting (integer).

### `get_subframe`
Returns current subframe region: `{width, height, x, y}`.

### `get_focuser_setting`
Returns autofocus settings.

### `get_dither`
Returns dither settings.

### `get_control_value`
```json
{"id": 1, "method": "get_control_value", "params": ["Exposure", true]}
```

### `clear_autosave_err`
Clears any autosave error state.

### `test_connection`
```json
{"id": 1, "method": "test_connection"}
```
Response:
```json
{"result": "server connected!", "code": 0, "id": 1}
```

---

### Unsolicited Events — Port 4700

#### `Version`
Sent immediately on connect — before any command is processed.
```json
{"Event":"Version","name":"ASI AIR imager","svr_ver_string":"1.0","firmware_ver_string":"13.41"}
```

#### `Temperature`
Pushed periodically with camera sensor temperature.
```json
{"Event": "Temperature", "value": -2.5}
```

#### `PiStatus`
Pushed periodically with Raspberry Pi system stats.
```json
{"Event": "PiStatus", "is_overtemp": false, "temp": 56.1, "is_undervolt": false, "is_over_current": false}
```

---

## Recommended Shutdown Sequence

1. Send `stop_exposure` on port 4700
2. Send `scope_park` on port 4400
3. Confirm with `scope_get_info` → `park_status: "parked"`
4. (Optional) Cut power via a Kasa smart plug
