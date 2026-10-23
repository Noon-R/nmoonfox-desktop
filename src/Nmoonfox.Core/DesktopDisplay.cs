namespace Nmoonfox.Core;

// WorkArea and actor positions use physical virtual-desktop pixels; preferences use DIPs.
public sealed record DesktopDisplay(string Id, DesktopBounds WorkArea, double Scale = 1, bool IsPrimary = false);
