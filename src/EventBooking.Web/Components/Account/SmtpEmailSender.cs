using System.Net.Mail;

using Microsoft.AspNetCore.Identity.UI.Services;

namespace EventBooking.Web.Components.Account;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly string host;
    private readonly int port;
    private readonly string senderAddress;

    public SmtpEmailSender(string host, int port, string senderAddress)
    {
        this.host = host;
        this.port = port;
        this.senderAddress = senderAddress;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        using var message = new MailMessage(senderAddress, email, subject, htmlMessage)
        {
            IsBodyHtml = true
        };

        using var client = new SmtpClient(host, port);
        await client.SendMailAsync(message);
    }
}
