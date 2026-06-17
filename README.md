# AsiAirController

A desktop app for remotely controlling a [ZWO ASI Air](https://www.zwoastro.com/product/asiair/) astrophotography device at a remote observatory over a WireGuard VPN. The primary use case is fully automated, unattended imaging sessions — starting the plan when the roof opens, monitoring conditions, and safely shutting down if the roof closes or conditions change.

---

## Features

### Core Controls
- **Stop Exposure** — immediately halts the current camera exposure
- **Park Mount** — sends the mount to its park position
- **Safe Shutdown** — stops exposure, parks the mount, and turns off the dew heater in sequence

### Auto Run
Fully automated session management — set it and forget it:
- Waits for the observatory roof to open, then starts the active imaging plan
- Polls the roof every minute; if it closes, triggers a full safe shutdown (exposure stopped, mount parked, dew heater off)
- If the plan completes naturally with the roof still open, just turns off the heater
- Live countdown to next roof check; status line shows current state at a glance
- Creates a Discord forum thread at the start of each session; all logs and preview images are posted into that thread for easy per-night review

### Roof Monitoring
- Dual-source roof status: queries the [Starfront API](https://status.starfront.space) and an optional local network-mounted file simultaneously — whichever has the most recent timestamp wins
- Standalone roof polling (independent of Auto Run) with a 5-minute interval and countdown timer
- One-click status check to see current roof state and last-update timestamp
- Starfront building ID is configurable in Settings

### Plan Management
- Lists all imaging plans stored on the ASI Air
- Displays active plan detail: targets, frame sequences (type, exposure, gain, binning, filter, repeat count), time remaining, data remaining, and schedule (dusk/dawn or fixed times)
- Switch the active plan with a single click
- Start or reset the active plan with a confirmation prompt showing how many frames have already been captured
- Live frame counter (completed / total) and dusk countdown while waiting for a scheduled start

### Live Preview
- Background loop polls capture state every second via the persistent ASI Air connection
- When an exposure finishes, automatically downloads the raw image from port 4800, debayers it, and displays it
- Manual exposure trigger with configurable duration and a live countdown
- Download progress bar sized against the expected compressed frame size
- Camera sensor temperature badge — polls every 30 seconds (with a 20-second startup delay), shows current temp and cooling percentage if the camera is a cooled model

### Weather & Dew Monitoring
- Always-running weather poll (every 2 minutes) — visible even when no plan is active
- Reads an optional local [Boltwood Cloud Sensor II](https://diffractionlimited.com/product/boltwood-cloud-sensor-ii/) file (space-delimited format)
- Displays temperature, dew point, dew margin, humidity, cloud conditions, and wind conditions
- °C / °F toggle (stored per-preference; margin threshold converts correctly between units)
- **Dew heater auto-control**: while a plan is running, automatically turns the Kasa-connected heater on/off based on a configurable dew margin threshold; turns the heater off when the plan stops

### Kasa Smart Plugs
- Authenticates to the TP-Link Kasa cloud API
- Lists all devices; smart strips are automatically expanded into individual outlets (each with its own alias)
- Three independently configurable outlets:
  - **Dew Heater** — manual toggle + auto-control from weather monitoring
  - **Camera Power** — manual toggle for camera/imaging rig outlet (Settings screen)
  - **ASI Air Power** — manual toggle for the ASI Air itself (Settings screen)
- Live on/off indicator for each outlet; credentials and selected outlets persist between launches

### Notifications
- Discord webhook support — posts log entries and preview images
- Creates a new forum-channel thread at the start of each Auto Run, keeping each night's activity in its own thread

### Settings
All settings persist to `~/.config/AsiAirController/settings.json` (macOS/Linux) or `%APPDATA%\AsiAirController\settings.json` (Windows):
- ASI Air IP address
- Roof status file path (optional local SMB mount) and Starfront building ID
- Kasa email, password, and selected outlets (dew heater, camera power, ASI Air power)
- Weather file path, dew margin threshold, and °C/°F preference
- Discord webhook URL
- Window size

---

## How It Works

### Command Transport — Persistent TCP Connections

The ASI Air exposes a JSON-RPC API over TCP. Earlier versions of this app used one-shot connections (open socket → write → close), but the ASI Air's official app keeps persistent connections open, and this approach is now mirrored here.

One `AsiAirConnection` is kept alive per port. Commands are sent with an integer `id`; the response dispatcher matches responses back to callers by that id. Unsolicited events (e.g. `Version`, `Temperature`, `PiStatus`, `ScopeHome`) are silently dropped unless a caller registered for that id.

Port 4700 (imaging) receives a `test_connection` heartbeat every 5 seconds — matching behavior observed in the official app via Wireshark. Port 4400 (mount) stays open without a heartbeat.

```csharp
// Conceptually: send a command, await its matched response
var result = await AsiAirClient.CallAsync(host, new Mount.ScopeGetInfo());
```

If the host changes or a connection drops, both connections are torn down and rebuilt on the next call.

**Why not one-shot?**  
Port 4700 pushes a `{"Event":"Version",...}` message the moment a client connects. The persistent model keeps a read loop running that absorbs these events; one-shot connections have to close immediately after writing to avoid consuming the Version event before the command response.

### Image Download — Port 4800 Binary Protocol

Raw images are downloaded over a separate one-off TCP connection to port 4800. The response is a binary blob with a JSON header followed by a ZIP archive containing a `raw_data` entry. The app detects the ZIP end-of-central-directory signature (`PK\x05\x06`) to know when the transfer is complete, then extracts and debayers the raw sensor data.

### Roof Status

Two sources are queried in parallel and the most recent timestamp wins:

1. **Starfront API**: `https://alpaca-api.tx.starfront.space/api/v1/roof/state` — returns a JSON array of buildings, each with `device_number`, `is_open`, and `state_update` (ISO 8601 UTC). Building is selected by the configured Starfront Building ID (default: 5).
2. **Local file** (optional): a plain-text file on an SMB share, format:
   ```
   2026-06-09 05:16:34AM CST Roof Status: CLOSED
   ```

If the status is anything other than `OPEN`, a safe shutdown fires. The shutdown triggers once per closed event; it resets when the roof reopens.

**Mounting the SMB share (macOS):**  
Finder → Go → Connect to Server → `smb://172.16.5.21/sfro-customer` (username: `guest`, no password). Once mounted the file is at `/Volumes/sfro-customer/roof/building-5/RoofStatusFile.txt`.

### Camera Temperature

`get_control_value Temperature` returns the sensor temperature as an integer in tenths of a degree (e.g. `299` = 29.9°C). `get_control_value CoolPowerPerc` gives the cooling percentage for cooled cameras. Both are polled every 30 seconds in a background task that starts 20 seconds after the preview loop connects, so it never delays the initial connection.

### Discord Forum Threads

When Auto Run starts, the app POSTs to `{webhook}?wait=true` with a `thread_name` field to create a new forum thread. The response `channel_id` is the thread ID, and all subsequent log and image posts for that session are routed to `?thread_id={id}`. The thread ID is cleared when Auto Run ends.

---

## Build & Run

Requires .NET SDK 9.x or later. The project sets `<RollForward>Major</RollForward>` so it also runs on a .NET 10 runtime.

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
| 4800 | Raw image download (binary protocol) |
| 4500 | Unknown |
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
| `capture.state` | e.g. `"idle"`, `"target_delay"` (waiting for dusk) |
| `capture.exposure_mode` | `"autosave"` when a plan is running |
| `capture.error` | Last error string (e.g. `"aborted"`) |
| `capture.lapse_ms` | Elapsed ms in current state |
| `capture.total_ms` | Total ms for current state (used for dusk countdown) |
| `capture.progress.cur_plan.lapse` | Completed frames in current sequence |
| `capture.progress.cur_plan.total` | Total frames in current sequence |
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
| `total_time_sec` / `left_time_sec` | Total and remaining plan time |
| `total_size_m` / `left_size_m` | Total and remaining data in MB |
| `start_time` / `end_time` | e.g. `{"type": "dusk"}` / `{"type": "dawn"}` |
| `targets[].target_name` | |
| `targets[].seqs[].type` | e.g. `"light"` |
| `targets[].seqs[].exp` | Exposure time in seconds |
| `targets[].seqs[].gain` | Gain (-1 = auto) |
| `targets[].seqs[].bin` | Binning |
| `targets[].seqs[].filter` | Filter index |
| `targets[].seqs[].repeat` | Frame count |
| `targets[].seqs[].lapsed` | Frames completed so far |

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
Enables one plan and disables all others atomically. Pass an array of `{id, enable}` objects.
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

Known control names: `Exposure`, `Temperature`, `CoolPowerPerc`.

`Temperature` returns an integer in tenths of a degree (e.g. `299` = 29.9°C).

### `set_control_value`
```json
{"id": 1, "method": "set_control_value", "params": ["Exposure", 10000000]}
```
Exposure is in microseconds (10,000,000 µs = 10 s).

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

## Port 4800 — Raw Image Download

Binary protocol, one-off connection per image. Send a JSON request, receive a binary blob containing a JSON header followed by a ZIP archive. The archive contains a `raw_data` entry with the raw Bayer sensor data.

Detection: scan for the ZIP local file header signature `PK\x03\x04`, skip the JSON prefix, then read until the end-of-central-directory signature `PK\x05\x06` is found in the tail of the buffer.

---

## Recommended Shutdown Sequence

1. Send `stop_exposure` on port 4700
2. Turn off dew heater via Kasa cloud API (if applicable)
3. Send `scope_park` on port 4400
4. Confirm with `scope_get_info` → `park_status: "parked"`
5. (Optional) Cut power via a Kasa smart plug
