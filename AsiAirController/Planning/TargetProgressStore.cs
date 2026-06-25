using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsiAirController.Planning;

/// <summary>
/// Tracks accumulated integration time per target across multiple nights.
/// Data is persisted as a JSON file in the same ApplicationData folder as settings.
/// Thread-safe for single-writer use (only AutoTargetPlanner writes).
/// </summary>
public class TargetProgressStore
{
    private readonly string _filePath;
    private Dictionary<string, TargetProgress> _data = new(StringComparer.OrdinalIgnoreCase);

    public TargetProgressStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultPath();
    }

    private static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AsiAirController");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "target_progress.json");
    }

    // ── Load / Save ───────────────────────────────────────────────────────────

    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            _data = JsonSerializer.Deserialize<Dictionary<string, TargetProgress>>(json,
                _options) ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _data = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, _options));
        }
        catch { /* best-effort */ }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public TargetProgress Get(string objectName)
    {
        _data.TryGetValue(objectName, out var p);
        return p ?? new TargetProgress { ObjectName = objectName };
    }

    public IEnumerable<TargetProgress> GetAll() => _data.Values;

    /// <summary>
    /// Record a completed imaging session for a target.
    /// hoursImaged is the wall-clock dark time devoted to this target tonight.
    /// </summary>
    public void RecordSession(string objectName, DateOnly date, double hoursImaged)
    {
        if (!_data.TryGetValue(objectName, out var p))
        {
            p = new TargetProgress { ObjectName = objectName };
            _data[objectName] = p;
        }

        p.TotalHours += hoursImaged;
        p.LastObservedDate = date;
        p.SessionCount++;
        p.Sessions.Add(new SessionRecord(date, hoursImaged));
        Save();
    }

    public double GetTotalHours(string objectName) => Get(objectName).TotalHours;

    public void Reset(string objectName)
    {
        _data.Remove(objectName);
        Save();
    }

    public void ResetAll()
    {
        _data.Clear();
        Save();
    }

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented    = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public class TargetProgress
{
    public required string ObjectName  { get; set; }
    public double   TotalHours         { get; set; }
    public int      SessionCount       { get; set; }
    public DateOnly? LastObservedDate  { get; set; }
    public List<SessionRecord> Sessions { get; set; } = [];
}

public record SessionRecord(DateOnly Date, double Hours);
