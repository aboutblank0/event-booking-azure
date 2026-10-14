using System.ComponentModel.DataAnnotations;
using EventBooking.Core;
using EventBooking.Web.Components.Events;

namespace EventBooking.Tests;

public class EventInputTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static EventInput ValidInput() => new()
    {
        Name = "Lisbon Tech Meetup",
        Description = "Talks and networking.",
        StartsAt = new DateTime(2026, 11, 14, 19, 0, 0),
        EndsAt = new DateTime(2026, 11, 14, 22, 0, 0),
        TimeZoneId = "Europe/Lisbon",
        VenueName = "Lisbon Congress Centre",
        Address = "Praça das Indústrias, Lisbon",
        Latitude = 38.70,
        Longitude = -9.20,
        Capacity = 100,
    };

    private static List<string> ValidationErrors(EventInput input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).ToList();
    }

    [Fact]
    public void ToDetails_ConvertsVenueTimesToExactMoments()
    {
        // Tokyo is UTC+9 all year.
        var input = ValidInput();
        input.TimeZoneId = "Asia/Tokyo";

        var details = input.ToDetails();

        Assert.Equal(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero), details.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 13, 0, 0, TimeSpan.Zero), details.EndsAt);
        Assert.Equal("Asia/Tokyo", details.TimeZoneId);
        Assert.Equal(100, details.Capacity);
    }

    [Fact]
    public void FromEvent_ShowsTheSavedValuesInVenueTime()
    {
        var details = ValidInput().ToDetails();
        var ev = Event.Create("organiser-1", details.Name, details.Description, details.StartsAt, details.EndsAt,
            details.TimeZoneId, details.VenueName, details.Address, details.Latitude, details.Longitude,
            details.Capacity, Now);

        var input = EventInput.FromEvent(ev);

        Assert.Equal(new DateTime(2026, 11, 14, 19, 0, 0), input.StartsAt);
        Assert.Equal(new DateTime(2026, 11, 14, 22, 0, 0), input.EndsAt);
        Assert.Equal(details, input.ToDetails());
    }

    [Fact]
    public void ToDetailsKeepingSchedule_UsesTheEventsScheduleAndTheFormsOtherValues()
    {
        var details = ValidInput().ToDetails();
        var ev = Event.Create("organiser-1", details.Name, details.Description, details.StartsAt, details.EndsAt,
            details.TimeZoneId, details.VenueName, details.Address, details.Latitude, details.Longitude,
            details.Capacity, Now);
        var input = ValidInput();
        input.StartsAt = new DateTime(2026, 12, 1, 9, 0, 0);
        input.TimeZoneId = "Asia/Tokyo";
        input.Name = "Renamed";

        var kept = input.ToDetailsKeepingSchedule(ev);

        Assert.Equal(ev.StartsAt, kept.StartsAt);
        Assert.Equal(ev.EndsAt, kept.EndsAt);
        Assert.Equal("Europe/Lisbon", kept.TimeZoneId);
        Assert.Equal("Renamed", kept.Name);
    }

    [Fact]
    public void Validation_ValidInput_HasNoErrors()
    {
        Assert.Empty(ValidationErrors(ValidInput()));
    }

    [Fact]
    public void Validation_EmptyForm_RequiresEveryField()
    {
        var errors = ValidationErrors(new EventInput { TimeZoneId = "" });

        string[] required =
        [
            nameof(EventInput.Name), nameof(EventInput.Description), nameof(EventInput.StartsAt),
            nameof(EventInput.EndsAt), nameof(EventInput.TimeZoneId), nameof(EventInput.VenueName),
            nameof(EventInput.Address), nameof(EventInput.Latitude), nameof(EventInput.Longitude),
            nameof(EventInput.Capacity),
        ];
        Assert.Equal(required.Order(), errors.Order());
    }

    [Fact]
    public void Validation_OutOfRangeValues_AreRejected()
    {
        var input = ValidInput();
        input.Latitude = 91;
        input.Longitude = -181;
        input.Capacity = 0;
        input.Name = new string('a', Event.NameMaxLength + 1);

        Assert.Equal(
            [nameof(EventInput.Capacity), nameof(EventInput.Latitude), nameof(EventInput.Longitude),
             nameof(EventInput.Name)],
            ValidationErrors(input).Order());
    }
}
