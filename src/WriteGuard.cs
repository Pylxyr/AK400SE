namespace DeepCoolLite;

/// <summary>Enforces a minimum time between two writes to the cooler. Time comes from a monotonic clock in milliseconds.</summary>
public sealed class WriteGuard
{
    public const int MinIntervalMs = 500;
    long _last = long.MinValue;

    /// <summary>True if a write may happen now (and records it). False if it is too soon, or the clock went backwards.</summary>
    public bool TryPass(long nowMs)
    {
        if (_last != long.MinValue)
        {
            if (nowMs < _last) { _last = nowMs; return false; }   // clock went backwards: refuse once, then continue from here
            if (nowMs - _last < MinIntervalMs) return false;
        }
        _last = nowMs;
        return true;
    }
}
