using EventBooking.Core;
using EventBooking.Web.Services;

namespace EventBooking.Tests;

public class VenueTimeTests
{
    [Fact]
    public void TimeZoneIds_AreIanaNames()
    {
        Assert.Contains("Europe/Lisbon", VenueTime.TimeZoneIds);
        Assert.Contains("America/New_York", VenueTime.TimeZoneIds);
        Assert.DoesNotContain("GMT Standard Time", VenueTime.TimeZoneIds);
    }

    [Fact]
    public void ToMoment_UsesTheVenueOffsetOnThatDate()
    {
        // Lisbon is UTC+1 in summer and UTC+0 in winter.
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 19, 0, 0, TimeSpan.FromHours(1)),
            VenueTime.ToMoment(new DateTime(2026, 7, 1, 19, 0, 0), "Europe/Lisbon"));
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 19, 0, 0, TimeSpan.Zero),
            VenueTime.ToMoment(new DateTime(2026, 12, 1, 19, 0, 0), "Europe/Lisbon"));
    }

    [Fact]
    public void ToMoment_TimeSkippedByClocksGoingForward_Throws()
    {
        // On 29 March 2026, Lisbon's clocks jump from 01:00 to 02:00.
        var ex = Assert.Throws<BusinessRuleException>(() =>
            VenueTime.ToMoment(new DateTime(2026, 3, 29, 1, 30, 0), "Europe/Lisbon"));

        Assert.Contains("clocks go forward", ex.Message);
    }

    [Fact]
    public void ToMoment_UnknownTimeZone_Throws()
    {
        Assert.Throws<BusinessRuleException>(() => VenueTime.ToMoment(new DateTime(2026, 7, 1), "Mars/Olympus"));
    }

    [Fact]
    public void ToLocal_IsTheReverseOfToMoment()
    {
        var local = new DateTime(2026, 11, 14, 19, 30, 0);

        Assert.Equal(local, VenueTime.ToLocal(VenueTime.ToMoment(local, "Asia/Tokyo"), "Asia/Tokyo"));
    }

    [Fact]
    public void FormatRange_SameDay_ShowsTheDateOnce()
    {
        var start = VenueTime.ToMoment(new DateTime(2026, 11, 14, 19, 0, 0), "Europe/Lisbon");

        Assert.Equal("Sat 14 Nov 2026, 19:00 – 22:00",
            VenueTime.FormatRange(start, start.AddHours(3), "Europe/Lisbon"));
    }

    [Fact]
    public void FormatRange_PastMidnight_ShowsBothDates()
    {
        var start = VenueTime.ToMoment(new DateTime(2026, 11, 14, 22, 0, 0), "Europe/Lisbon");

        Assert.Equal("Sat 14 Nov 2026, 22:00 – Sun 15 Nov 2026, 04:00",
            VenueTime.FormatRange(start, start.AddHours(6), "Europe/Lisbon"));
    }
}
