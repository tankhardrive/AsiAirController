namespace AsiAirController.Planning;

public class DeepSkyObject
{
    public required string Name { get; init; }
    public ObjectType Type { get; init; }

    /// <summary>J2000 right ascension in decimal degrees (0–360).</summary>
    public double RaDegrees { get; init; }

    /// <summary>J2000 declination in decimal degrees (-90–+90).</summary>
    public double DecDegrees { get; init; }

    /// <summary>RA in decimal hours (0–24). Use this when sending to ASI Air.</summary>
    public double RaHours => RaDegrees / 15.0;

    public string Constellation { get; init; } = "";

    /// <summary>Major axis in arcminutes.</summary>
    public double? MajorAxisArcmin { get; init; }
    public double? MinorAxisArcmin { get; init; }
    public double? PositionAngleDeg { get; init; }
    public double? MagnitudeB { get; init; }
    public double? MagnitudeV { get; init; }
    public double? SurfaceBrightness { get; init; }
    public string? HubbleType { get; init; }

    public int? MessierNumber { get; init; }
    public int? CaldwellNumber { get; init; }
    public string? CommonName { get; init; }

    public CatalogSource Catalogs { get; init; }

    public double DisplayMagnitude => MagnitudeV ?? MagnitudeB ?? 99.0;

    public string SizeDisplay
    {
        get
        {
            if (MajorAxisArcmin is null) return "";
            if (MinorAxisArcmin is null || Math.Abs(MajorAxisArcmin.Value - MinorAxisArcmin.Value) < 0.01)
                return $"{MajorAxisArcmin:F1}'";
            return $"{MajorAxisArcmin:F1}' × {MinorAxisArcmin:F1}'";
        }
    }

    public string DisplayName =>
        !string.IsNullOrEmpty(CommonName) ? CommonName :
        MessierNumber.HasValue ? $"M{MessierNumber}" :
        ShortName;

    public string ShortName
    {
        get
        {
            if (Name.StartsWith("NGC"))
            {
                var suffix = Name[3..];
                return int.TryParse(suffix, out int n) ? $"NGC {n}" : $"NGC {suffix.TrimStart('0')}";
            }
            if (Name.StartsWith("IC"))
            {
                var suffix = Name[2..];
                return int.TryParse(suffix, out int n) ? $"IC {n}" : $"IC {suffix.TrimStart('0')}";
            }
            return Name;
        }
    }

    public string CatalogIds
    {
        get
        {
            var parts = new List<string>();
            if (MessierNumber.HasValue)  parts.Add($"M{MessierNumber}");
            if (CaldwellNumber.HasValue) parts.Add($"C{CaldwellNumber}");
            parts.Add(ShortName);
            return string.Join(" / ", parts);
        }
    }
}
