using System;

namespace Sonokinesis.Loader.Runtime;

/// <summary>
/// A smooth song time for drawing. The game's music position only advances once per audio buffer
/// (about every 21 ms), so drawing straight from it makes hazards hold still for a few frames and
/// then jump. This clock runs on frame time and is gently pulled towards the music, so motion is
/// smooth while staying in sync. Seeks, rewinds and pauses snap it back to the music.
/// </summary>
internal sealed class SongClock
{
    /// <summary>Errors larger than this are jumps (seek, rewind, checkpoint) rather than drift.</summary>
    private const double SnapMs = 120;
    /// <summary>The buffer step is at most ~43 ms on slow audio settings; never extrapolate past it.</summary>
    private const double MaxExtrapolateMs = 50;
    /// <summary>Fraction of the remaining error corrected per 1/60 s.</summary>
    private const double Pull = 0.12;

    private double smooth = double.NaN;
    private double lastRaw = double.NaN;
    private double rawChangedAt;

    public void Reset() { smooth = double.NaN; lastRaw = double.NaN; }

    /// <param name="raw">Song time read from the music this frame.</param>
    /// <param name="now">Wall clock in ms (unscaled).</param>
    /// <param name="dtMs">Frame time in ms (unscaled).</param>
    /// <param name="frozen">Paused or rewinding: follow the music exactly.</param>
    public double Next(double raw, double now, double dtMs, bool frozen)
    {
        if (raw != lastRaw) { lastRaw = raw; rawChangedAt = now; }
        if (frozen || double.IsNaN(smooth)) return smooth = raw;

        // Where the music most likely is now: the last reading plus the time since it changed.
        double estimate = raw + Math.Min(now - rawChangedAt, MaxExtrapolateMs);
        double next = smooth + dtMs;
        double error = estimate - next;
        if (Math.Abs(error) > SnapMs) return smooth = raw;
        next += error * (1 - Math.Pow(1 - Pull, dtMs / (1000.0 / 60)));
        // Never run backwards because of a correction; real jumps snap above.
        return smooth = Math.Max(next, smooth);
    }
}
