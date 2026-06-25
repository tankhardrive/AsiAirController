using System.Globalization;

namespace AsiAirController.Services;


/// <summary>
/// Base for all ASI Air JSON-RPC commands. Each instance gets a unique auto-incremented id.
/// Call ToRequestJson() to get the wire payload.
/// </summary>
public abstract record AsiAirCommand
{
    private static int _nextId;
    public int Id { get; } = Interlocked.Increment(ref _nextId);

    public abstract int    Port   { get; }
    public abstract string Method { get; }

    protected virtual string? ParamsJson => null;

    internal string ToRequestJson() => ParamsJson is null
        ? $"{{\"id\":{Id},\"method\":\"{Method}\"}}\n"
        : $"{{\"id\":{Id},\"method\":\"{Method}\",\"params\":{ParamsJson}}}\n";
}

// ─── Capture / camera (port 4700) ─────────────────────────────────────────────

public static class Capture
{
    public record GetAppState() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_app_state";
    }

    /// <param name="FrameType">"light", "dark", "flat", "bias"</param>
    public record StartExposure(string FrameType = "light") : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "start_exposure";
        protected override string? ParamsJson => $"[\"{FrameType}\"]";
    }

    public record StopExposure() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "stop_exposure";
    }

    /// <param name="Page">"preview" or "plan"</param>
    public record SetPage(string Page) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "set_page";
        protected override string? ParamsJson => $"[\"{Page}\"]";
    }

    /// <param name="Control">e.g. "Exposure" (value in microseconds)</param>
    public record SetControlValue(string Control, double Value, bool Auto = false) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "set_control_value";
        protected override string? ParamsJson =>
            $"[\"{Control}\",{Value.ToString(CultureInfo.InvariantCulture)},{(Auto ? "true" : "false")}]";
    }

    public record GetControlValue(string Control) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_control_value";
        protected override string? ParamsJson => $"[\"{Control}\"]";
    }

    public record GetCameraInfo() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_camera_info";
    }

    public record GetControls() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_controls";
    }

    public record GetCameraState() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_camera_state";
    }

    // get_camera_bin: legacy; use GetCameraBinning for bin + max_bin
    public record GetCameraBin() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_camera_bin";
    }

    // result: {"bin":1,"max_bin":2}
    public record GetCameraBinning() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_camera_binning";
    }

    public record GetGain() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_gain";
    }

    public record GetGainSegment() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_gain_segment";
    }

    public record GetSubframe() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_subframe";
    }

    public record GetExposure() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_exposure";
    }

    public record GetSetting() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_setting";
    }

    // result: {"main_camera_name":"ZWO ASI2600MC Duo", ...}
    public record GetAppSetting() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_app_setting";
    }

    // params: [{"key": value}]  e.g. [{"main_camera_name":"ZWO ASI2600MC Duo"}]
    public record SetAppSetting(string SettingJson) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "set_app_setting";
        protected override string? ParamsJson => $"[{SettingJson}]";
    }

    public record GetImageSavePath() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_image_save_path";
    }

    // result: {"totalMB":227272,"freeMB":219496}
    public record GetDiskVolume() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_disk_volume";
    }

    public record GetFocalLength() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_focal_length";
    }

    public record SetFocalLength(double Mm) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "set_focal_length";
        protected override string? ParamsJson =>
            $"[{Mm.ToString(CultureInfo.InvariantCulture)}]";
    }

    public record GetDawnDuskTime() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_dawn_dusk_time";
    }

    public record GetDither() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_dither";
    }

    public record GetStackSetting() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_stack_setting";
    }

    public record GetFuncState() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_func_state";
    }

    public record GetLastSolveResult() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_last_solve_result";
    }

    public record GetRtmpConfig() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_rtmp_config";
    }

    public record GetConnected() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_connected";
    }

    public record GetConnectedCameras() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_connected_cameras";
    }

    public record OpenCamera(int Index) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "open_camera";
        protected override string? ParamsJson => $"[{Index}]";
    }

    public record TestConnection() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "test_connection";
    }
}

// ─── Focuser (port 4700) ──────────────────────────────────────────────────────

public static class Focuser
{
    // result: [{"name":"EAF","id":0}]
    public record GetConnectedFocuser() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_connected_focuser";
    }

    // result: "USB"
    public record GetFocuserConnectMode() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_focuser_connect_mode";
    }

    // result: {"position":5295,"sn":"...","temperature":34.82,"firmware_version":"3.3.8",
    //          "max_step":60000,"backlash":15,"model":"EAF-0-0","state":"idle", ...}
    public record GetFocuserInfo() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_focuser_info";
    }

    // result: {"af_exp_sec":1.0,"af_init_step":30,"af_bin":1,"af_only_one":false,
    //          "fine_step":100,"coarse_step":500,"autosave":{...},"stack":{...}}
    public record GetFocuserSetting() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_focuser_setting";
    }

    // result: {"state":"idle","name":"EAF","id":0,"max_step_range":600000}
    public record GetFocuserState() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_focuser_state";
    }
}

// ─── Filter wheel (port 4700) ─────────────────────────────────────────────────

public static class FilterWheel
{
    // result: [] (empty if no wheel connected)
    public record GetConnectedWheels() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_connected_wheels";
    }

    // result: {"state":"close"}
    public record GetWheelState() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_wheel_state";
    }
}

// ─── Device / Pi system (port 4700) ───────────────────────────────────────────

public static class Device
{
    // result: {"is_pi4":true,"model":"ZWO AirPlus-RK3568 (Linux)","uname":"...","guid":"...",
    //          "cpuId":"...","temp":39.44,"is_undervolt":false,"is_over_current":false,"is_has_ble":true}
    public record PiGetInfo() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "pi_get_info";
    }

    // result: {"cable_connected":true,"ip":"x.x.x.x","gateway":"x.x.x.x",
    //          "netmask":"255.255.252.0","dhcp":true,"static_ip":""}
    public record PiEth0State() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "pi_eth0_state";
    }

    // result: {"ssid":"ASIAIR_PLUS_xxxx","passwd":"..."}
    public record PiGetAp() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "pi_get_ap";
    }

    // result: {"is_5g":false,"channel":11,"freq":2462}
    public record PiGetApChannel() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "pi_get_ap_channel";
    }

    // result: {"server":false}
    public record PiStationState() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "pi_station_state";
    }

    // result: [[voltage, current], ...] — one entry per power output channel
    // observed 5 channels: [main_12v, ch2, ch3, ch4, usb_12v]
    public record PiOutputGet2() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "pi_output_get2";
    }

    // result: {"disable_meridian_limit":false}
    public record GetBetaSetting() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_beta_setting";
    }

    // result: {"new_solve_exec":false,"autogoto_threshold":0.1,"auto_open_heater":true,
    //          "auto_open_cooler":false,"station_5g":false,"plan_simulate":false,"goto_simulate":false}
    public record GetTestSetting() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_test_setting";
    }
}

// ─── Camera angle adapter (port 4700) ─────────────────────────────────────────

public static class Caa
{
    // result: {"connected":[],"state":{"state":"close"},"controls":{},
    //          "solve_angle":{"update_timestamp":...,"current_angle":91.99},
    //          "setting":{"auto_align_mosaic":true,"caa_angle_limit":false}}
    public record GetCaaInfo() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_caa_info";
    }

    // result: {"is_pa_3p":false,"rotate_angle":20}
    public record Get3pPaSetting() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_3p_pa_setting";
    }
}

// ─── Plan management (port 4700) ──────────────────────────────────────────────
//
// All create/edit operations use import_plan with different payload shapes.
// The ASI Air treats import_plan as a general upsert: fields present are
// applied, fields absent are left unchanged.

// Shared sub-types used by plan create/edit commands.

/// <summary>
/// When plan imaging starts.
/// type: "none" | "dusk" | "specific"
///   - "none"     → start immediately when plan is activated
///   - "dusk"     → start at astronomical dusk (planTimeModelType=3)
///   - "specific" → start at a wall-clock time; set Hour (0-23) and Minute (0-59)
/// </summary>
public record PlanStartTime(string Type, int Hour = 0, int Minute = 0);

/// <summary>
/// When plan imaging ends.
/// type: "none" | "dawn" | "specific"
///   - "none"     → run until all frames complete
///   - "dawn"     → stop at astronomical dawn (planTimeModelType=2)
///   - "specific" → stop at a wall-clock time; set Hour (0-23) and Minute (0-59)
/// </summary>
public record PlanEndTime(string Type, int Hour = 0, int Minute = 0);

/// <summary>
/// One imaging sequence slot inside a target.
/// </summary>
public record PlanSequence(
    int    Id,           // 1-based; use next available id within the target's seqs list
    string FrameType,    // "light" | "dark" | "flat" | "bias"
    int    Filter,       // 0=Lum/first wheel slot; 1,2,3… = subsequent slots
    double ExpSec,       // exposure in seconds
    int    Gain,         // camera gain; -10000 = use camera default
    int    Bin,          // binning: 1=1×1, 2=2×2
    int    Repeat,       // frame count
    bool   AutoExp,      // true = ASI Air picks exposure automatically (for flats)
    bool   Enable = true,
    int    CaptureIndex = 1);

/// <summary>
/// A plan target — one sky object with RA/Dec and its imaging sequences.
/// </summary>
public record PlanTarget(
    int    Id,                    // 1-based; use next available id within the plan's targets list
    string Name,                  // display name (e.g. "M31")
    double RaHours,               // right ascension in decimal hours (0–24)
    double DecDegrees,            // declination in decimal degrees (-90–+90)
    IReadOnlyList<PlanSequence> Sequences,
    bool   Enable = true,
    string OriginalName = "",     // catalog name if from catalog; same as Name for custom
    string OriginalImg  = "",     // catalog thumbnail filename (e.g. "caldwell-c6.heic"); empty for custom
    int    CaptureIndex = 1);

public static class Plan
{
    private static string B(bool v) => v ? "true" : "false";
    private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public record ListPlan() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "list_plan";
    }

    public record GetEnabledPlan() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_enabled_plan";
    }

    /// <summary>
    /// Toggle which plan is active. Sends the full plan list with each plan's new enabled state.
    /// Every plan in the list must be included; omitted plans keep their prior state.
    /// result: 0 on success.
    /// </summary>
    public record ImportPlan(IReadOnlyList<(int Id, bool Enable)> Plans) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "import_plan";
        protected override string? ParamsJson =>
            "[" + string.Join(",", Plans.Select(p =>
                $"{{\"id\":{p.Id},\"enable\":{(p.Enable ? "true" : "false")}}}")) + "]";
    }

    /// <summary>
    /// Create a new plan or update an existing plan's metadata and schedule settings.
    /// To create: pass Id = max(existing_ids) + 1 (ASI Air uses client-supplied id as the key).
    /// result: 0 on success.
    /// </summary>
    /// <param name="PlanId">Plan id. For a new plan: max existing id + 1.</param>
    /// <param name="Name">Plan display name.</param>
    /// <param name="StartTime">When to start imaging (none/dusk/specific).</param>
    /// <param name="EndTime">When to stop imaging (none/dawn/specific).</param>
    /// <param name="Enable">Whether this plan is the active (enabled) one.</param>
    /// <param name="MeridianFlip">Allow mount meridian flip mid-plan.</param>
    /// <param name="MountGoHome">Park mount when plan ends.</param>
    /// <param name="StartGuiding">Start guiding automatically when plan runs.</param>
    /// <param name="CloseCooler">Turn off camera cooler at end of plan.</param>
    /// <param name="WaitForCooling">Wait for camera to reach target temp before starting.</param>
    /// <param name="ShutdownPi">Power off the ASI Air Pi at plan end.</param>
    public record CreateOrUpdatePlan(
        int          PlanId,
        string       Name,
        PlanStartTime StartTime,
        PlanEndTime   EndTime,
        bool         Enable        = false,
        bool         MeridianFlip  = true,
        bool         MountGoHome   = true,
        bool         StartGuiding  = true,
        bool         CloseCooler   = true,
        bool         WaitForCooling = true,
        bool         ShutdownPi    = false) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "import_plan";
        protected override string? ParamsJson
        {
            get
            {
                var startJson = BuildTimeJson(StartTime, isStart: true);
                var endJson   = BuildTimeJson(EndTime,   isStart: false);
                return $"[{{\"id\":{PlanId},\"plan_name\":{Plan.Q(Name)},\"enable\":{Plan.B(Enable)}," +
                       $"\"start_time\":{startJson},\"end_time\":{endJson}," +
                       $"\"merid_flip\":{Plan.B(MeridianFlip)},\"mount_go_home\":{Plan.B(MountGoHome)}," +
                       $"\"start_guide\":{Plan.B(StartGuiding)},\"close_cooler\":{Plan.B(CloseCooler)}," +
                       $"\"wait_cooling\":{Plan.B(WaitForCooling)},\"shutdown_pi\":{Plan.B(ShutdownPi)}}}]";
            }
        }

        private static string BuildTimeJson(PlanStartTime t, bool isStart) => t.Type switch
        {
            "dusk"     => $"{{\"type\":\"dusk\",\"planTimeModelType\":3,\"hour\":0}}",
            "dawn"     => $"{{\"type\":\"dawn\",\"planTimeModelType\":2,\"hour\":0}}",
            "specific" => $"{{\"type\":\"specific\",\"planTimeModelType\":1,\"hour\":{t.Hour},\"minute\":{t.Minute}}}",
            _          => "{\"type\":\"none\"}"
        };

        private static string BuildTimeJson(PlanEndTime t, bool isStart) => t.Type switch
        {
            "dawn"     => $"{{\"type\":\"dawn\",\"planTimeModelType\":2,\"hour\":0}}",
            "dusk"     => $"{{\"type\":\"dusk\",\"planTimeModelType\":3,\"hour\":0}}",
            "specific" => $"{{\"type\":\"specific\",\"planTimeModelType\":1,\"hour\":{t.Hour},\"minute\":{t.Minute}}}",
            _          => "{\"type\":\"none\"}"
        };
    }

    /// <summary>
    /// Add or replace a target (sky object + RA/Dec) within a plan.
    /// Sequences are set to empty — use ImportPlanSequences afterwards to add them.
    /// result: 0 on success.
    /// </summary>
    /// <param name="PlanId">The plan to add the target to.</param>
    /// <param name="Target">Target definition.</param>
    public record ImportPlanTarget(int PlanId, PlanTarget Target) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "import_plan";
        protected override string? ParamsJson =>
            $"[{{\"id\":{PlanId},\"targets\":[{TargetJson(Target)}]}}]";

        private static string TargetJson(PlanTarget t) =>
            $"{{\"id\":{t.Id},\"capture_index\":{t.CaptureIndex},\"enable\":{Plan.B(t.Enable)}," +
            $"\"target_name\":{Plan.Q(t.Name)},\"target_original_name\":{Plan.Q(t.OriginalName)}," +
            $"\"target_original_img\":{Plan.Q(t.OriginalImg)}," +
            $"\"target_coord\":[{t.RaHours.ToString("G", CultureInfo.InvariantCulture)}," +
            $"{t.DecDegrees.ToString("G", CultureInfo.InvariantCulture)}]}}";
    }

    /// <summary>
    /// Set the complete sequence list for one target within a plan.
    /// Replaces all existing sequences on that target.
    /// result: 0 on success.
    /// </summary>
    /// <param name="PlanId">The plan that owns the target.</param>
    /// <param name="TargetId">The target to update sequences for.</param>
    /// <param name="Sequences">Full replacement sequence list.</param>
    public record ImportPlanSequences(
        int PlanId,
        int TargetId,
        IReadOnlyList<PlanSequence> Sequences) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "import_plan";
        protected override string? ParamsJson =>
            $"[{{\"id\":{PlanId},\"targets\":[{{\"id\":{TargetId},\"seqs\":[" +
            string.Join(",", Sequences.Select(SeqJson)) +
            "]}}]}}]";

        private static string SeqJson(PlanSequence s) =>
            $"{{\"id\":{s.Id},\"capture_index\":{s.CaptureIndex},\"enable\":{Plan.B(s.Enable)}," +
            $"\"type\":{Plan.Q(s.FrameType)},\"filter\":{s.Filter}," +
            $"\"exp\":{s.ExpSec.ToString("G", CultureInfo.InvariantCulture)}," +
            $"\"gain\":{s.Gain},\"bin\":{s.Bin},\"repeat\":{s.Repeat}," +
            $"\"autoexp\":{Plan.B(s.AutoExp)}}}";
    }

    public record ResetPlan(int PlanId) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "reset_plan";
        protected override string? ParamsJson => $"[{{\"plan_id\":{PlanId}}}]";
    }

    public record GetTargetSequences(int PlanId, int TargetId) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_target_sequences";
        protected override string? ParamsJson =>
            $"[{{\"plan_id\":{PlanId},\"target_id\":{TargetId}}}]";
    }

    /// <summary>
    /// Toggle enabled/disabled for individual targets within a plan.
    /// result: 0 on success.
    /// </summary>
    public record SetPlan(int PlanId, IReadOnlyList<(int TargetId, bool Enable)> Targets) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "set_plan";
        protected override string? ParamsJson =>
            "[{\"id\":" + PlanId + ",\"targets\":[" +
            string.Join(",", Targets.Select(t =>
                $"{{\"id\":{t.TargetId},\"enable\":{(t.Enable ? 1 : 0)}}}")) +
            "]}]";
    }
}

// ─── Target catalog / sky map (port 4700) ─────────────────────────────────────

public static class Catalog
{
    // result: [{"list_id":1,"name":"favorite","count":1,"is_favorite":true}]
    public record GetCustomList() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_custom_list";
    }

    // result: [] or [{"body_id":N,"name":"...","ra":...,"dec":...}]
    public record GetCustomBody() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_custom_body";
    }

    // result: [{"body_id":1,"type":0}]
    public record GetBodyFromList(int ListId) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_body_from_list";
        protected override string? ParamsJson => $"{{\"list_id\":{ListId}}}";
    }
}

// ─── Mount (port 4400) ────────────────────────────────────────────────────────

public static class Mount
{
    public record TestConnection() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "test_connection";
    }

    public record ScopePark() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_park";
    }

    public record ScopeGetInfo() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_get_info";
    }

    public record ScopeGetRaDec() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_get_ra_dec";
    }

    // result: [altitude_deg, azimuth_deg]
    public record ScopeGetHorizCoord() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_get_horiz_coord";
    }

    public record ScopeGetTrackState() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_get_track_state";
    }

    public record ScopeGetSlewRate() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_get_slew_rate";
    }

    public record ScopeIsMoving() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_is_moving";
    }

    public record ScopeGetInputVoltage() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_get_input_voltage";
    }

    public record ScopeGetCap() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_get_cap";
    }

    public record ScopeGetConnectionMode() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_get_connection_mode";
    }

    public record ScopeGetConnectionPara() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_get_connection_para";
    }

    public record ScopeSetLocation(double Lat, double Lon) : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "scope_set_location";
        protected override string? ParamsJson =>
            $"[{{\"lat\":{Lat.ToString(CultureInfo.InvariantCulture)},\"lon\":{Lon.ToString(CultureInfo.InvariantCulture)}}}]";
    }

    public record GetMountList() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "get_mount_list";
    }

    public record GetMountIndex() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "get_mount_index";
    }

    public record SelectMountListIndex(int Index) : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "select_mount_list_index";
        protected override string? ParamsJson => $"[{Index}]";
    }

    public record MountScanPort() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "mount_scan_port";
    }

    // result: {"fw_ver":"1.8.8","sn":"xxxxxxxxxxxx","model":"ZWO AM5","ble_name":"","can_change_ble_name":false}
    public record GetConnectedMountInfo() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "get_connected_mount_info";
    }
}

// ─── Guiding (port 4400) ──────────────────────────────────────────────────────

public static class Guiding
{
    // result: "Auto" | "North" | "South" | "Off"
    public record GetDecGuideMode() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "get_dec_guide_mode";
    }

    // params: ["ra"|"dec", "aggression"]  result: 0.75 (float, 0–1 range)
    public record GetAlgoParam(string Axis, string ParamName) : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "get_algo_param";
        protected override string? ParamsJson => $"[\"{Axis}\",\"{ParamName}\"]";
    }

    // result: 50 (int, search region radius in pixels)
    public record GetSearchRegion() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "get_search_region";
    }

    // result: true/false
    public record GetAutoLoadCalibration() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "get_auto_load_calibration";
    }

    // result: array of {ra, dec, timestamp} history points for the guiding graph
    public record GetRaDecHistory() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "get_ra_dec_history";
    }

    // result: 0  (triggers one guiding loop iteration)
    public record Loop() : AsiAirCommand
    {
        public override int Port => 4400;
        public override string Method => "loop";
    }
}

// ─── Image download (port 4800) ───────────────────────────────────────────────

public static class Image
{
    /// <remarks>
    /// Response is a streaming ZIP (binary), not a standard JSON-RPC reply.
    /// AsiAirClient.FetchRawImageAsync handles the custom read loop.
    /// </remarks>
    public record GetCurrentImage() : AsiAirCommand
    {
        public override int Port => 4800;
        public override string Method => "get_current_img";
    }
}
