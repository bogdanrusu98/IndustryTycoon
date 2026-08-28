using System;

namespace IndustryTycoon.Persistence
{
    public interface IUtcClock
    {
        long UtcNowUnixSeconds { get; }
    }

    public sealed class SystemUtcClock : IUtcClock
    {
        public static readonly SystemUtcClock Instance = new SystemUtcClock();

        private SystemUtcClock()
        {
        }

        public long UtcNowUnixSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Deterministic clock for tests and explicit development-only time injection.
    /// Runtime composition should use SystemUtcClock unless QA intentionally overrides it.
    /// </summary>
    public sealed class ManualUtcClock : IUtcClock
    {
        public ManualUtcClock(long utcNowUnixSeconds)
        {
            UtcNowUnixSeconds = utcNowUnixSeconds;
        }

        public long UtcNowUnixSeconds { get; set; }
    }
}
