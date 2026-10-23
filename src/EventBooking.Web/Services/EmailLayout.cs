namespace EventBooking.Web.Services;

/// <summary>
/// The shared look of every email the site sends. Styles are inline because most email
/// clients ignore &lt;style&gt; blocks, and the layout is a table because Outlook ignores
/// max-width on a div.
/// </summary>
public static class EmailLayout
{
    public static string Wrap(string bodyHtml) => $"""
        <!DOCTYPE html>
        <html>
        <body style="margin:0;padding:24px 12px;background:#f4f5f7;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#212529;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #dee2e6;border-radius:8px;">
        <tr><td style="padding:16px 24px;border-bottom:1px solid #dee2e6;font-size:16px;font-weight:600;">Event Booking</td></tr>
        <tr><td style="padding:8px 24px 16px;font-size:15px;line-height:1.5;">{bodyHtml}</td></tr>
        </table>
        <p style="max-width:560px;margin:16px auto 0;font-size:12px;color:#6c757d;text-align:center;">You're receiving this because you have an account on Event Booking.</p>
        </body>
        </html>
        """;

    /// <summary>A link styled as a button. The href must already be HTML-encoded.</summary>
    public static string Button(string href, string text) =>
        $"""<p style="margin:24px 0;"><a href="{href}" style="display:inline-block;padding:10px 20px;background:#0d6efd;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600;">{text}</a></p>""";
}
