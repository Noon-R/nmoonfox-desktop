namespace Nmoonfox.Core;

/// <summary>Recognizes deliberate back-and-forth movement from DIP positions and monotonic seconds.</summary>
public sealed class MouseShakeDetector
{
    private bool initialized;
    private double previousX, previousY, previousTime;
    private double windowStart, pathLength, directionX, directionY, strokeLength;
    private double nextAllowedTime;
    private double lastFastTime = double.NegativeInfinity;
    private int reversals;

    public void Reset()
    {
        initialized = false;
        ClearGesture();
    }

    public bool Sample(double x, double y, double timestamp)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(timestamp))
        { Reset(); return false; }
        if (!initialized)
        {
            initialized = true; previousX = x; previousY = y; previousTime = timestamp;
            return false;
        }
        var delta = timestamp - previousTime;
        var dx = x - previousX; var dy = y - previousY;
        previousX = x; previousY = y; previousTime = timestamp;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (delta <= 0 || delta > .16 || distance > 450)
        { ClearGesture(); return false; }
        if (timestamp < nextAllowedTime) { ClearGesture(); return false; }
        if (distance / delta < 300)
        {
            // A hand briefly slows down at each turning point. Preserve a short pause,
            // but never combine separate ordinary movements into a gesture.
            if (timestamp - lastFastTime > .12) ClearGesture();
            return false;
        }
        if (distance < 3) return false;
        if (pathLength == 0 || timestamp - windowStart > .65)
        {
            ClearGesture(); windowStart = timestamp;
            directionX = dx / distance; directionY = dy / distance;
        }
        var dot = (dx * directionX + dy * directionY) / distance;
        if (dot < -.5)
        {
            if (strokeLength >= 22) reversals++;
            directionX = dx / distance; directionY = dy / distance;
            strokeLength = 0;
        }
        strokeLength += distance;
        pathLength += distance;
        lastFastTime = timestamp;
        if (reversals < 2 || pathLength < 120 || strokeLength < 22) return false;
        nextAllowedTime = timestamp + 1.5;
        ClearGesture();
        return true;
    }

    private void ClearGesture()
    {
        pathLength = 0; strokeLength = 0; reversals = 0;
        directionX = 0; directionY = 0;
        lastFastTime = double.NegativeInfinity;
    }
}
