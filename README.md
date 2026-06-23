# AsiAirController

A macOS desktop app for remotely controlling a [ZWO ASI Air](https://www.zwoastro.com/product/asiair/) astrophotography device at a remote observatory over a WireGuard VPN. The primary use case is fully automated, unattended imaging sessions — starting the plan when the roof opens, monitoring conditions, recovering from cloud gaps, and safely shutting down at dawn.

> **Status:** Early testing / pre-release. Built for a specific remote observatory setup (Starfront TX) but designed to be configurable for other sites.

---

## Features

### Core Controls
- **Stop Exposure** — immediately halts the current camera exposure
- **Park Mount** — sends the mount to its park position
- **Safe Shutdown** — stops exposure, parks the mount, and turns off the dew heater in sequence

### Auto Run
Fully automated session management:
- Waits for the observatory roof to open, then starts the active imaging plan
- Shows live "Waiting for start · imaging at HH:mm" status when the plan has a scheduled start time
- **Cloud gap recovery** — if the roof closes mid-session, the mount parks and the exposure stops, but the camera stays cooled and the dew heater remains on; the loop keeps monitoring and automatically restarts the plan when the roof reopens
- Fetches tonight's dawn time from the ASI Air and ends the session automatically at dawn (full shutdown + image sync)
- If the plan completes naturally with the roof still open, turns off the dew heater and runs image sync
- Live countdown to next roof check; status line shows current state at a glance
- Creates a Discord forum thread at the start of each session; all logs and preview images are posted into that thread for easy per-night review

### Autopilot
Multi-night fully automated operation — powers up the hardware, runs a night, and powers down, then repeats:
- Build a **night queue** of plans in any order; the queue cycles automatically each night
- Set the number of nights to run (0 = run forever)
- Powers on the camera outlet, waits 5 seconds, then powers on the ASI Air; waits for TCP connection before starting
- Fetches real dusk/dawn times from the ASI Air once connected and uses them for precise scheduling
- Runs the full Auto Run session for the night (including cloud gap recovery)
- After the plan completes: syncs images, then powers down ASI Air then camera (5-second gap between)
- Configurable **power-on offset** (minutes before dusk to wake the hardware)
- Active sessions shown in a purple banner at the top of the window
- Requires Kasa smart plugs configured for both Camera Power and ASI Air Power outlets

### Camera Cooling
- **Pre-cool**: automatically starts camera cooling a configurable number of minutes before the plan's scheduled start time
- Waits for the ASI Air's TargetDelay event to determine the exact start time before making the cooling decision — no premature cooling while waiting for dark
- Turns off cooling at session end

### Plan Management
- Lists all imaging plans stored on the ASI Air
- Displays active plan detail: targets, frame sequences (type, exposure, gain, binning, filter, repeat count), time remaining, data remaining, and schedule (dusk/dawn or fixed times)
- Switch the active plan with a single click
- Start or reset the active plan with a confirmation prompt showing how many frames have already been captured
- Live frame counter (completed / total) and scheduled-start countdown while waiting for a delayed start

### Live Preview
- Background loop polls capture state every second via the persistent ASI Air connection
- When an exposure finishes, automatically downloads the raw image from port 4800, debayers it, and displays it
- Manual exposure trigger with configurable duration and a live countdown
- Download progress bar sized against the expected compressed frame size
- Camera sensor temperature and cooling percentage badge (cooled cameras only), polled every 30 seconds

### Image Sync
- At the end of each Auto Run session (plan complete or dawn reached), optionally syncs FITS files from a source folder to a destination folder
- Compares files by size — skips anything already fully copied
- 3-pass retry with increasing delays for files that fail (handles SMB latency / partial writes)
- Configurable source and destination paths in Settings; folder picker for easy selection

### Weather & Dew Monitoring
- Always-running weather poll (every 2 minutes) — visible even when no plan is active
- Reads an optional local [Boltwood Cloud Sensor II](https://diffractionlimited.com/product/boltwood-cloud-sensor-ii/) file (space-delimited format)
- Displays temperature, dew point, dew margin, humidity, cloud conditions, and wind conditions
- °C / °F toggle (stored per-preference; margin threshold converts correctly)
- **Dew heater auto-control**: while a plan is running, automatically turns the Kasa-connected heater on/off based on a configurable dew margin threshold; turns the heater off when the plan stops

### Roof Monitoring
- Dual-source roof status: queries the [Starfront API](https://status.starfront.space) and an optional local network-mounted file simultaneously — whichever has the most recent timestamp wins
- Standalone roof polling (independent of Auto Run) with a 5-minute interval and countdown timer
- One-click status check to see current roof state and last-update timestamp
- Starfront building ID is configurable in Settings

### Kasa Smart Plugs
- Authenticates to the TP-Link Kasa cloud API
- Lists all devices; smart strips are automatically expanded into individual outlets (each with its own alias)
- Three independently configurable outlets:
  - **Dew Heater** — manual toggle + auto-control from weather monitoring
  - **Camera Power** — manual toggle + used by Autopilot for power sequencing
  - **ASI Air Power** — manual toggle + used by Autopilot for power sequencing
- Live on/off indicator for each outlet; credentials and selected outlets persist between launches

### Notifications
- Discord webhook support — posts log entries and preview images
- Creates a new forum-channel thread at the start of each Auto Run, keeping each night's activity in its own thread

---

## Build & Run

### Prerequisites
- [.NET SDK 9](https://dotnet.microsoft.com/download/dotnet/9) or later
- macOS (primary target; Windows/Linux may work but are untested)

### Clone and run

```bash
git clone https://github.com/tankhardrive/AsiAirController.git
cd AsiAirController
dotnet run --project AsiAirController/AsiAirController.csproj
```

Or build a release binary:

```bash
dotnet publish AsiAirController/AsiAirController.csproj \
  -c Release -r osx-arm64 --self-contained
```

Output lands in `AsiAirController/bin/Release/net9.0/osx-arm64/publish/`.

> **Note for Rider users:** Avalonia source generators don't run via `dotnet build` alone, so `MainWindow.axaml.cs` has an explicit constructor calling `InitializeComponent()`. This is intentional and required.

### First launch

1. Enter your **ASI Air IP address** (find it in the ASI Air app under Wi-Fi settings)
2. *(Optional)* Enter **Kasa credentials** to enable smart plug control
3. *(Optional)* Configure a **Roof Status File** path or **Starfront Building ID** for roof monitoring
4. *(Optional)* Set a **Discord Webhook URL** for notifications

All settings persist automatically to:
- macOS/Linux: `~/.config/AsiAirController/settings.json`
- Windows: `%APPDATA%\AsiAirController\settings.json`

---

## Settings Reference

| Setting | Description |
|---------|-------------|
| ASI Air IP | IP address of the ASI Air on your local/VPN network |
| Starfront Building ID | ID of your building in the Starfront API (default: 5) |
| Roof Status File | Path to a local SMB-mounted roof status text file |
| Kasa Email / Password | TP-Link Kasa cloud credentials |
| Dew Heater outlet | Which Kasa outlet controls the dew heater |
| Camera Power outlet | Which Kasa outlet controls the camera/imaging rig |
| ASI Air Power outlet | Which Kasa outlet controls the ASI Air itself |
| Weather File | Path to a Boltwood Cloud Sensor II data file |
| Dew Margin Threshold | °C/°F below which the heater auto-turns on |
| Temperature Unit | °C or °F |
| Pre-cool Minutes | How many minutes before plan start to begin camera cooling |
| Image Sync Source | Folder on the ASI Air / NAS to copy FITS files from |
| Image Sync Destination | Local folder to copy FITS files into |
| Discord Webhook URL | Webhook for log and image notifications |
| Mount Location | Lat/lon read from the ASI Air (display only — confirms GPS lock) |

**Autopilot settings** (on the Autopilot tab):
| Setting | Description |
|---------|-------------|
| Night Queue | Ordered list of plans to run, one per night, cycling |
| Number of nights | Total nights to run (0 = infinite) |
| Power on offset | Minutes before estimated dusk to power on hardware |

---

## Remote Observatory Setup (Starfront TX)

This app was built for a specific setup but the roof/weather integrations are configurable:

**Roof status file** (SMB share):
```
smb://172.16.5.21/sfro-customer  (username: guest, no password)
```
Once mounted, the file is at:
```
/Volumes/sfro-customer/roof/building-5/RoofStatusFile.txt
```
Format: `2026-06-09 05:16:34AM CST Roof Status: CLOSED`

**Starfront API**: `https://alpaca-api.tx.starfront.space/api/v1/roof/state`
Returns a JSON array of buildings, each with `device_number`, `is_open`, and `state_update` (ISO 8601 UTC).

---

## How It Works

### Command Transport — Persistent TCP Connections

The ASI Air exposes a JSON-RPC API over TCP. The app keeps a persistent `AsiAirConnection` per port. Commands are sent with an integer `id`; the response dispatcher matches responses back to callers by that id. Unsolicited events (e.g. `Version`, `Temperature`, `PiStatus`, `ScopeHome`) are handled by the event loop.

Port 4700 (imaging) receives a `test_connection` heartbeat every 5 seconds — matching behavior observed in the official app. Port 4400 (mount) stays open without a heartbeat.

```csharp
// Send a command, await its matched response
var result = await AsiAirClient.CallAsync(host, new Mount.ScopeGetInfo());
```

If the host changes or a connection drops, both connections are torn down and rebuilt on the next call.

### Image Download — Port 4800 Binary Protocol

Raw images are downloaded over a separate one-off TCP connection to port 4800. The response is a binary blob: a JSON header followed by a ZIP archive containing a `raw_data` entry. The app detects the ZIP end-of-central-directory signature (`PK\x05\x06`) to know when the transfer is complete, then extracts and debayers the raw sensor data.

### Dawn/Dusk & Timezone Handling

Dawn and dusk times are fetched from the ASI Air via `get_dawn_dusk_time`. Because the ASI Air device may be in a different timezone than the user's computer, all comparisons are done in UTC using `DateTimeOffset.FromUnixTimeSeconds` — never local time on either machine.

### Cloud Gap Recovery

When the roof closes mid-session:
1. The current exposure is stopped and the mount is parked
2. Camera cooling and dew heater are left on (preserving thermal stability)
3. The Auto Run loop continues checking the roof every minute
4. If the roof reopens before dawn, the plan is restarted from scratch
5. If dawn arrives before the roof reopens, full shutdown runs and image sync fires

### Discord Forum Threads

When Auto Run starts, the app POSTs to `{webhook}?wait=true` with a `thread_name` field to create a new forum thread. The response `channel_id` is the thread ID, and all subsequent log and image posts for that session are routed to `?thread_id={id}`. The thread ID is cleared when Auto Run ends.

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

### `scope_get_connection_para`
Returns observatory location and mount connection parameters.
```json
{"id": 1, "method": "scope_get_connection_para"}
```
Key fields: `lat`, `lon` (decimal degrees).

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
| `capture.state` | e.g. `"idle"`, `"target_delay"` (waiting for scheduled start) |
| `capture.exposure_mode` | `"autosave"` when a plan is running |
| `capture.error` | Last error string (e.g. `"aborted"`) |
| `capture.lapse_ms` | Elapsed ms in current state |
| `capture.total_ms` | Total ms for current state |
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
Returns tonight's dawn and dusk times. Values may be Unix timestamps or decimal hours depending on firmware version — parse defensively.
```json
{"id": 1, "method": "get_dawn_dusk_time"}
```
Response:
```json
{"result": {"dawn": 5.831866, "dusk": 23.345425}}
```
> **Timezone note:** The ASI Air returns times in its own local timezone, which may differ from the controlling computer. Always convert to UTC before comparing with wall-clock time.

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

#### `TargetDelay`
Pushed when a plan is waiting for its scheduled start time.
```json
{"Event": "TargetDelay", "state": "start", "seconds": 3600}
{"Event": "TargetDelay", "state": "end"}
```
`seconds` is the number of seconds remaining until the plan starts imaging.

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
