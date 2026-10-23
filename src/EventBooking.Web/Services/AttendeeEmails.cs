using System.Net;
using EventBooking.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace EventBooking.Web.Services;

/// <summary>
/// Emails attendees about their bookings. Called after the change is saved; a failed email is
/// logged and ignored, so it never undoes or blocks the change.
/// </summary>
public class AttendeeEmails(IEmailSender emailSender, NavigationManager navigation, ILogger<AttendeeEmails> logger)
{
    public Task SendBookedAsync(string email, Event ev) =>
        SendAsync(email, $"Booking confirmed: {ev.Name}",
            $"<p>You have booked a place at <strong>{Encode(ev.Name)}</strong>.</p>"
            + Details(ev) + Link(EventPath(ev), "View the event"));

    public Task SendBookingCancelledAsync(string email, Event ev) =>
        SendAsync(email, $"Booking cancelled: {ev.Name}",
            $"<p>You have cancelled your booking for <strong>{Encode(ev.Name)}</strong>.</p>"
            + Details(ev) + Link(EventPath(ev), "View the event"));

    public async Task SendEventCancelledAsync(IEnumerable<string> emails, Event ev)
    {
        foreach (var email in emails)
            await SendAsync(email, $"Event cancelled: {ev.Name}",
                $"<p>Sorry, <strong>{Encode(ev.Name)}</strong> has been cancelled and won't take place.</p>"
                + Details(ev) + Link("my-bookings", "See your bookings"));
    }

    public async Task SendVenueChangedAsync(IEnumerable<string> emails, Event ev)
    {
        foreach (var email in emails)
            await SendAsync(email, $"New venue: {ev.Name}",
                $"<p>The venue for <strong>{Encode(ev.Name)}</strong> has changed. Here are the updated details.</p>"
                + Details(ev) + Link(EventPath(ev), "View the event"));
    }

    private async Task SendAsync(string email, string subject, string htmlMessage)
    {
        try
        {
            await emailSender.SendEmailAsync(email, subject, EmailLayout.Wrap(htmlMessage));
        }
        catch (Exception ex)
        {
            // The address is left out of the log, as it's personal data.
            logger.LogWarning(ex, "Couldn't send the email \"{Subject}\".", subject);
        }
    }

    private static string Details(Event ev) =>
        $"<p>{VenueTime.FormatRange(ev.StartsAt, ev.EndsAt, ev.TimeZoneId)} ({Encode(ev.TimeZoneId)} time)<br>"
        + $"{Encode(ev.VenueName)}, {Encode(ev.Address)}</p>";

    private static string EventPath(Event ev) => $"events/{ev.Id}";

    // Emails are read outside the site, so links need the full address.
    private string Link(string path, string text) =>
        EmailLayout.Button(navigation.ToAbsoluteUri(path).ToString(), text);

    // Organisers type these values, so HTML in them must show as text.
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
