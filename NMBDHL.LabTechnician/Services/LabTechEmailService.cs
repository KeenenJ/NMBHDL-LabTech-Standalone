using System.Net;
using System.Net.Mail;
using NMBHDL.LabTechnician.Configuration;
using MailKit.Security;
using MimeKit;

namespace NMBHDL.LabTechnician.Services;

public sealed class LabTechEmailService
{
    private readonly LabTechOptions _options;

    public LabTechEmailService(LabTechOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // Validate the email details and send or log the message using the configured delivery mode
    public async Task<(bool Success, string Message)> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)

    // Validate the recipient, subject and email body before processing the message
    {
        if (string.IsNullOrWhiteSpace(toEmail) ||
            !MailAddress.TryCreate(toEmail.Trim(), out var recipient))
        {
            return (false, "A valid recipient email address is required.");
        }

        if (string.IsNullOrWhiteSpace(subject))
            return (false, "An email subject is required.");

        if (string.IsNullOrWhiteSpace(htmlBody))
            return (false, "An email body is required.");

        // Load the email configuration and determine the delivery mode.
        var emailOptions = _options.Email;
        var mode = emailOptions.Mode?.Trim();

        // Log the email details without delivering a message
        if (string.Equals(mode, "LogOnly", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"[LabTech email - LogOnly] To: {recipient.Address}; Subject: {subject}");

            return (true, "Email logged only; no message was delivered.");
        }

        if (string.IsNullOrWhiteSpace(emailOptions.SenderEmail) ||
            !MailAddress.TryCreate(emailOptions.SenderEmail.Trim(), out var sender))
        {
            return (false, "Configure a valid Lab Technician sender email address.");
        }

        // Prepare the email sender, recipient, subject and HTML body.
        using var message = new MailMessage
        {
            From = new MailAddress(
                sender.Address,
                emailOptions.SenderName ?? string.Empty),
            Subject = subject.Trim(),
            Body = htmlBody,
            IsBodyHtml = true
        };

        message.To.Add(recipient);
        // Save the email to a local pickup directory for testing or later delivery
        try
        {
            if (string.Equals(mode, "Pickup", StringComparison.OrdinalIgnoreCase))
            {
                var pickupDirectory = emailOptions.PickupDirectory;

                if (string.IsNullOrWhiteSpace(pickupDirectory))
                    return (false, "Configure a local email pickup directory.");

                if (!Path.IsPathRooted(pickupDirectory))
                {
                    return (false,
                        "The pickup directory must be an absolute path for this service.");
                }

                Directory.CreateDirectory(pickupDirectory);

                using var client = new SmtpClient
                {
                    DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                    PickupDirectoryLocation = pickupDirectory
                };

                await client.SendMailAsync(message, cancellationToken);

                return (true, "Email saved to the local pickup directory.");
            }

            // Connect to the SMTP server, authenticate and send the email
            if (string.Equals(mode, "Smtp", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(emailOptions.SmtpHost))
                    return (false, "Configure an SMTP host before sending email.");

                if (emailOptions.SmtpPort is < 1 or > 65535)
                    return (false, "Configure a valid SMTP port.");

                if (string.IsNullOrWhiteSpace(emailOptions.Username) ||
                    string.IsNullOrWhiteSpace(emailOptions.Password))
                {
                    return (false, "Configure valid SMTP credentials.");
                }

                var emailMessage = new MimeMessage();

                emailMessage.From.Add(
                    new MailboxAddress(
                        emailOptions.SenderName ?? string.Empty,
                        sender.Address));

                emailMessage.To.Add(
                    new MailboxAddress(
                        recipient.DisplayName ?? string.Empty,
                        recipient.Address));

                emailMessage.Subject = subject.Trim();
                emailMessage.Body = new TextPart("html")
                {
                    Text = htmlBody
                };

                using var smtpClient = new MailKit.Net.Smtp.SmtpClient();

                await smtpClient.ConnectAsync(
                    emailOptions.SmtpHost,
                    emailOptions.SmtpPort,
                    SecureSocketOptions.StartTls,
                    cancellationToken);

                await smtpClient.AuthenticateAsync(
                    emailOptions.Username.Trim(),
                    emailOptions.Password,
                    cancellationToken);

                await smtpClient.SendAsync(
                    emailMessage,
                    cancellationToken);

                await smtpClient.DisconnectAsync(
                    true,
                    cancellationToken);

                return (true, "Email sent successfully.");
            }


            return (false,
                "Unsupported email mode. Use LogOnly, Pickup, or Smtp.");
        }
        // Allow cancellation to propagate while handling other delivery errors
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Lab Technician email delivery failed: {ex}");
            return (false, "Email delivery failed. Check the application logs.");
        }
    }
}
