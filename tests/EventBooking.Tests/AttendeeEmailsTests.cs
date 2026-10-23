using EventBooking.Core;

namespace EventBooking.Tests;

public class AttendeeEmailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeEmailSender _sender = new();

    private static Event CreateEvent(string name = "Lisbon Tech Meetup", string venueName = "Lisbon Congress Centre")
    {
        // 18:00 UTC is 19:00 in Lisbon (summer time).
        var startsAt = new DateTimeOffset(2027, 6, 5, 18, 0, 0, TimeSpan.Zero);
        return Event.Create("organiser-id", name, "Talks and networking.", startsAt, startsAt.AddHours(3),
            "Europe/Lisbon", venueName, "Rua das Flores 10, Lisbon", 38.70, -9.20, 10, Now);
    }

    [Fact]
    public async Task Booked_HasTheEventDetailsAtVenueTimeAndAFullLink()
    {
        await _sender.CreateAttendeeEmails().SendBookedAsync("ana@example.com", CreateEvent());

        var sent = Assert.Single(_sender.Sent);
        Assert.Equal("ana@example.com", sent.To);
        Assert.Equal("Booking confirmed: Lisbon Tech Meetup", sent.Subject);
        Assert.Contains("Sat 5 Jun 2027, 19:00 – 22:00 (Europe/Lisbon time)", sent.HtmlMessage);
        Assert.Contains("Lisbon Congress Centre, Rua das Flores 10, Lisbon", sent.HtmlMessage);
        Assert.Contains("href=\"https://events.example/events/0\"", sent.HtmlMessage);
    }

    [Fact]
    public async Task Emails_UseTheSharedLayoutWithAButton()
    {
        await _sender.CreateAttendeeEmails().SendBookedAsync("ana@example.com", CreateEvent());

        var html = Assert.Single(_sender.Sent).HtmlMessage;
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains(">View the event</a>", html);
        Assert.Contains("you have an account on Event Booking", html);
    }

    [Fact]
    public async Task OrganiserText_IsHtmlEncoded()
    {
        var ev = CreateEvent(name: "<b>Party</b>", venueName: "Tom & Jerry's");

        await _sender.CreateAttendeeEmails().SendBookedAsync("ana@example.com", ev);

        var html = Assert.Single(_sender.Sent).HtmlMessage;
        Assert.Contains("&lt;b&gt;Party&lt;/b&gt;", html);
        Assert.Contains("Tom &amp; Jerry&#39;s", html);
        Assert.DoesNotContain("<b>Party", html);
    }

    [Fact]
    public async Task EventCancelled_GoesToEachAttendeeAndLinksToTheirBookings()
    {
        await _sender.CreateAttendeeEmails().SendEventCancelledAsync(["ana@example.com", "rui@example.com"], CreateEvent());

        Assert.Equal(["ana@example.com", "rui@example.com"], _sender.Sent.Select(e => e.To));
        Assert.All(_sender.Sent, e => Assert.Contains("https://events.example/my-bookings", e.HtmlMessage));
    }

    [Fact]
    public async Task FailedEmails_AreIgnored()
    {
        _sender.Fail = true;

        await _sender.CreateAttendeeEmails().SendVenueChangedAsync(["ana@example.com", "rui@example.com"], CreateEvent());

        Assert.Empty(_sender.Sent);
    }
}
