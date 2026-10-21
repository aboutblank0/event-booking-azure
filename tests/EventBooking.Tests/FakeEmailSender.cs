using EventBooking.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventBooking.Tests;

public record SentEmail(string To, string Subject, string HtmlMessage);

/// <summary>Records emails instead of sending them.</summary>
internal class FakeEmailSender : IEmailSender
{
    public List<SentEmail> Sent { get; } = [];

    /// <summary>When true, every send throws, like Azure rejecting an email.</summary>
    public bool Fail { get; set; }

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (Fail)
            throw new InvalidOperationException("Email rejected.");
        Sent.Add(new SentEmail(email, subject, htmlMessage));
        return Task.CompletedTask;
    }

    /// <summary>Attendee emails sent through this fake, with links on https://events.example/.</summary>
    public AttendeeEmails CreateAttendeeEmails() =>
        new(this, new FakeNavigationManager(), NullLogger<AttendeeEmails>.Instance);

    // Outside a web request nothing sets the site's address, so this does.
    private class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager() => Initialize("https://events.example/", "https://events.example/");
    }
}
