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

    public record GetCameraBin() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_camera_bin";
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

    public record GetAppSetting() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_app_setting";
    }

    public record GetImageSavePath() : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "get_image_save_path";
    }

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

// ─── Plan management (port 4700) ──────────────────────────────────────────────

public static class Plan
{
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

    /// <param name="Plans">Full plan list: every plan with its new enabled state.</param>
    public record ImportPlan(IReadOnlyList<(int Id, bool Enable)> Plans) : AsiAirCommand
    {
        public override int Port => 4700;
        public override string Method => "import_plan";
        protected override string? ParamsJson =>
            "[" + string.Join(",", Plans.Select(p =>
                $"{{\"id\":{p.Id},\"enable\":{(p.Enable ? "true" : "false")}}}")) + "]";
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
}

// ─── Mount (port 4400) ────────────────────────────────────────────────────────

public static class Mount
{
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
