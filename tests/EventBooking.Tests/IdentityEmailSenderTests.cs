using EventBooking.Web.Components.Account;
using EventBooking.Web.Data;

namespace EventBooking.Tests;

public class IdentityEmailSenderTests
{
    private readonly FakeEmailSender _sender = new();
    private readonly ApplicationUser _user = new() { Email = "ana@example.com" };

    private IdentityEmailSender CreateSender() => new(_sender);

    [Fact]
    public async Task ConfirmationLink_IsAButtonInTheSharedLayout()
    {
        // The account pages HTML-encode the link before passing it in.
        const string link = "https://events.example/Account/ConfirmEmail?userId=1&amp;code=abc";

        await CreateSender().SendConfirmationLinkAsync(_user, "ana@example.com", link);

        var sent = Assert.Single(_sender.Sent);
        Assert.Equal("Confirm your email", sent.Subject);
        Assert.Contains($"href=\"{link}\"", sent.HtmlMessage);
        Assert.Contains(">Confirm email</a>", sent.HtmlMessage);
        Assert.Contains("you have an account on Event Booking", sent.HtmlMessage);
    }

    [Fact]
    public async Task PasswordResetLink_IsNotEncodedAgain()
    {
        const string link = "https://events.example/Account/ResetPassword?code=a&amp;b";

        await CreateSender().SendPasswordResetLinkAsync(_user, "ana@example.com", link);

        var html = Assert.Single(_sender.Sent).HtmlMessage;
        Assert.Contains($"href=\"{link}\"", html);
        Assert.DoesNotContain("&amp;amp;", html);
    }

    [Fact]
    public async Task PasswordResetCode_IsHtmlEncoded()
    {
        await CreateSender().SendPasswordResetCodeAsync(_user, "ana@example.com", "<code>");

        var html = Assert.Single(_sender.Sent).HtmlMessage;
        Assert.Contains("&lt;code&gt;", html);
        Assert.DoesNotContain("<code>", html);
    }
}
