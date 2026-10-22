using System.Text.Json;

namespace Nmoonfox.Core;

public sealed record PetSettings
{
    public int Count { get; init; } = 3;
    public double Size { get; init; } = 144;
    public double Speed { get; init; } = 55;
    public bool AlwaysOnTop { get; init; } = true;
    public bool RoamEverywhere { get; init; }
    public bool ClickThrough { get; init; }
    public bool YieldToMouse { get; init; } = true;

    public PetSettings Normalize() => this with
    {
        Count = Math.Clamp(Count, 0, 12),
        Size = double.IsFinite(Size) ? Math.Clamp(Size, 96, 240) : 144,
        Speed = double.IsFinite(Speed) ? Math.Clamp(Speed, 20, 140) : 55
    };
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public PetSettings Load()
    {
        try { return (JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(path)) ?? new()).Normalize(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return new(); }
    }

    public void Save(PetSettings settings)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Normalize(), Options));
        File.Move(temporary, fullPath, overwrite: true);
    }
}
