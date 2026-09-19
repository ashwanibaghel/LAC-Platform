namespace LAC.Infrastructure;

public interface IOfficeClock
{
    DateOnly GetCurrentDate();
    DateTimeOffset GetUtcNow();
}

public sealed class OfficeClock(TimeProvider? timeProvider = null) : IOfficeClock
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private static readonly TimeZoneInfo DelhiZone = GetDelhiTimeZone();

    private static TimeZoneInfo GetDelhiTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }

    public DateOnly GetCurrentDate()
    {
        var utcNow = _timeProvider.GetUtcNow();
        var delhiTime = TimeZoneInfo.ConvertTime(utcNow, DelhiZone);
        return DateOnly.FromDateTime(delhiTime.DateTime);
    }

    public DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();
}
