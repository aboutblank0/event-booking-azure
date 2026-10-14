using System.ComponentModel.DataAnnotations;
using EventBooking.Core;
using EventBooking.Web.Services;

namespace EventBooking.Web.Components.Events;

/// <summary>
/// The event form fields. Times are as shown on the venue's clocks. Core checks the rules again on submit.
/// </summary>
public class EventInput
{
    public const string DefaultTimeZoneId = "Europe/Lisbon";

    [Required]
    [StringLength(Event.NameMaxLength)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(Event.DescriptionMaxLength)]
    public string Description { get; set; } = "";

    [Required]
    [Display(Name = "Starts")]
    public DateTime? StartsAt { get; set; }

    [Required]
    [Display(Name = "Ends")]
    public DateTime? EndsAt { get; set; }

    [Required]
    [Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    [Required]
    [StringLength(Event.VenueNameMaxLength)]
    [Display(Name = "Venue name")]
    public string VenueName { get; set; } = "";

    [Required]
    [StringLength(Event.AddressMaxLength)]
    public string Address { get; set; } = "";

    [Required]
    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Required]
    [Range(-180, 180)]
    public double? Longitude { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int? Capacity { get; set; }

    public static EventInput FromEvent(Event ev) => new()
    {
        Name = ev.Name,
        Description = ev.Description,
        StartsAt = VenueTime.ToLocal(ev.StartsAt, ev.TimeZoneId),
        EndsAt = VenueTime.ToLocal(ev.EndsAt, ev.TimeZoneId),
        TimeZoneId = ev.TimeZoneId,
        VenueName = ev.VenueName,
        Address = ev.Address,
        Latitude = ev.Latitude,
        Longitude = ev.Longitude,
        Capacity = ev.Capacity,
    };

    /// <summary>Call after validation, which makes sure the required values are there.</summary>
    public EventDetails ToDetails() => ToDetails(
        VenueTime.ToMoment(StartsAt!.Value, TimeZoneId),
        VenueTime.ToMoment(EndsAt!.Value, TimeZoneId),
        TimeZoneId);

    /// <summary>
    /// For a published event, whose schedule can't change: keeps the saved moments rather than
    /// converting the form's clock times back, which could pick the other side of a clock change.
    /// </summary>
    public EventDetails ToDetailsKeepingSchedule(Event ev) => ToDetails(ev.StartsAt, ev.EndsAt, ev.TimeZoneId);

    private EventDetails ToDetails(DateTimeOffset startsAt, DateTimeOffset endsAt, string timeZoneId) => new(
        Name, Description, startsAt, endsAt, timeZoneId, VenueName, Address,
        Latitude!.Value, Longitude!.Value, Capacity!.Value);
}
