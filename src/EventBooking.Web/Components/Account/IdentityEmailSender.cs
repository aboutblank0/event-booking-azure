using System.Net;
using EventBooking.Web.Data;
using EventBooking.Web.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace EventBooking.Web.Components.Account;

public sealed class IdentityEmailSender : IEmailSender<ApplicationUser>
{
    private readonly IEmailSender emailSender;

    public IdentityEmailSender(IEmailSender emailSender)
    {
        this.emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
    }

    // The links arrive already HTML-encoded by the account pages.
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "Confirm your email",
            "<p>Please confirm your email address to use it with your Event Booking account.</p>"
            + EmailLayout.Button(confirmationLink, "Confirm email")
            + IgnoreIfNotYou);

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "Reset your password",
            "<p>We received a request to reset your password.</p>"
            + EmailLayout.Button(resetLink, "Reset password")
            + IgnoreIfNotYou);

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "Reset your password",
            "<p>Use this code to reset your password:</p>"
            + $"<p style=\"font-size:20px;font-weight:600;letter-spacing:2px;\">{WebUtility.HtmlEncode(resetCode)}</p>"
            + IgnoreIfNotYou);

    private const string IgnoreIfNotYou = "<p style=\"color:#6c757d;\">If you didn't ask for this, you can ignore this email.</p>";

    private Task SendAsync(string email, string subject, string bodyHtml) =>
        emailSender.SendEmailAsync(email, subject, EmailLayout.Wrap(bodyHtml));
}
