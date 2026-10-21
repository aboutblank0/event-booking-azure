using Azure;
using Azure.Communication.Email;

using Microsoft.AspNetCore.Identity.UI.Services;

namespace EventBooking.Web.Components.Account;

public sealed class AzureEmailSender : IEmailSender
{
    private readonly EmailClient emailClient;
    private readonly string senderAddress;

    public AzureEmailSender(EmailClient emailClient, string senderAddress)
    {
        this.emailClient = emailClient;
        this.senderAddress = senderAddress;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var message = new EmailMessage(senderAddress, email, new EmailContent(subject) { Html = htmlMessage });
        await emailClient.SendAsync(WaitUntil.Started, message);
    }
}
