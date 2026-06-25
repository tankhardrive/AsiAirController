namespace AsiAirController.Planning;

public enum FilterType { Broadband, Ha, Oiii, Sii, Custom }

public class ImagingSetup
{
    public string Name { get; set; } = "My Setup";

    public string TelescopeName { get; set; } = "";
    public double ApertureMm    { get; set; }
    public double FocalLengthMm { get; set; }

    public string CameraName        { get; set; } = "";
    public double PixelSizeMicrons  { get; set; }
    public int    SensorWidthPixels  { get; set; }
    public int    SensorHeightPixels { get; set; }

    public double QePercent           { get; set; } = 65;
    public double ReadNoiseElectrons  { get; set; } = 3.0;
    public double SubExposureSeconds  { get; set; } = 300;

    public FilterType Filter                  { get; set; } = FilterType.Broadband;
    public double     CustomFilterBandwidthNm { get; set; } = 7;

    public double FilterBandwidthNm => Filter switch
    {
        FilterType.Ha     => 7,
        FilterType.Oiii   => 8,
        FilterType.Sii    => 8,
        FilterType.Custom => CustomFilterBandwidthNm > 0 ? CustomFilterBandwidthNm : 7,
        _                 => 300,
    };

    public string FilterLabel => Filter switch
    {
        FilterType.Ha     => "Hα",
        FilterType.Oiii   => "OIII",
        FilterType.Sii    => "SII",
        FilterType.Custom => $"{CustomFilterBandwidthNm:F0}nm",
        _                 => "BB",
    };
}
