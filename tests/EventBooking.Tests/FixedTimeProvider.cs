namespace EventBooking.Tests;

/// <summary>A clock that always says the given time.</summary>
internal class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
