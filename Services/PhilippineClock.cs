namespace TrailGuard.Services;

public interface IPhilippineClock
{
    DateOnly Today { get; }
}

/// <summary>Provides the calendar date in the Philippines from an injectable UTC clock.</summary>
public sealed class PhilippineClock : IPhilippineClock
{
    private static readonly TimeZoneInfo PhilippineTimeZone = ResolvePhilippineTimeZone();
    private readonly TimeProvider _timeProvider;

    public PhilippineClock(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), PhilippineTimeZone).DateTime);

    private static TimeZoneInfo ResolvePhilippineTimeZone()
    {
        foreach (var id in new[] { "Asia/Manila", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        throw new InvalidOperationException("The Asia/Manila timezone is not available on this host.");
    }
}
