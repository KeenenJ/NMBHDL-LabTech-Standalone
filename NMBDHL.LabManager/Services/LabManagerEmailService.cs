#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NMBHLabSys.LabManagerSubsystem.Models;

namespace NMBHLabSys.LabManagerSubsystem.Services;

/// <summary>
/// Every email the Lab Manager subsystem sends. Each method returns false (and logs why)
/// instead of throwing, so a mail problem never undoes a saved record: the controller
/// saves first, then emails, and tells the manager if the email didn't go.
/// </summary>
public interface ILabManagerEmailService
{
    Task<bool> SendNewAccountAsync(string toEmail, string fullName, string roleLabel, string temporaryPassword, string loginUrl);
    Task<bool> SendPasswordResetByManagerAsync(string toEmail, string fullName, string temporaryPassword, string loginUrl);
    Task<bool> SendForgotPasswordLinkAsync(string toEmail, string resetLink, int validForMinutes);
    Task<bool> SendOrderPlacedAsync(OrderRow order);
    Task<bool> SendOrderCancelledAsync(OrderRow order, string reason);
    Task<bool> SendOrderItemCancelledAsync(OrderRow order, OrderItemRow item, string reason);
}

/// <summary>
/// Uses System.Net.Mail (built into .NET), so there is no extra package to install.
/// Mode "Pickup" (default) saves each email as an .eml file instead of sending it,
/// which is the easiest way to read temporary passwords and reset links while testing.
/// </summary>
public class LabManagerEmailService : ILabManagerEmailService
{
    private readonly LabManagerEmailOptions _settings;
    private readonly string _labName;
    private readonly string _pickupDirectory;
    private readonly ILogger<LabManagerEmailService> _logger;

    public LabManagerEmailService(IOptions<LabManagerOptions> options, IWebHostEnvironment env,
        ILogger<LabManagerEmailService> logger)
    {
        _settings = options.Value.Email;
        _labName = options.Value.LaboratoryName;
        _logger = logger;
        _pickupDirectory = Path.IsPathRooted(_settings.PickupDirectory)
            ? _settings.PickupDirectory
            : Path.Combine(env.ContentRootPath, _settings.PickupDirectory);
    }

    // ------------------------------------------------------------------ staff accounts

    // AC-06-02 / AC-07-02
    public Task<bool> SendNewAccountAsync(string toEmail, string fullName, string roleLabel, string temporaryPassword, string loginUrl) =>
        SendAsync(toEmail, $"Your {_labName} account",
            $@"<p>Dear {E(fullName)},</p>
               <p>A {E(roleLabel)} account has been created for you on NMB-HLabSys.</p>
               <p><strong>Email:</strong> {E(toEmail)}<br/>
                  <strong>Temporary password:</strong> <code>{E(temporaryPassword)}</code></p>
               <p>You will be asked to choose a new password the first time you log in.</p>
               <p><a href=""{E(loginUrl)}"">Log in to NMB-HLabSys</a></p>
               {Signature()}");

    public Task<bool> SendPasswordResetByManagerAsync(string toEmail, string fullName, string temporaryPassword, string loginUrl) =>
        SendAsync(toEmail, "Your NMB-HLabSys password has been reset",
            $@"<p>Dear {E(fullName)},</p>
               <p>The laboratory manager has reset your password.</p>
               <p><strong>Temporary password:</strong> <code>{E(temporaryPassword)}</code></p>
               <p>You will be asked to choose a new password when you log in.</p>
               <p><a href=""{E(loginUrl)}"">Log in to NMB-HLabSys</a></p>
               {Signature()}");

    // AC-09-03 / TC-LM-09-03
    public Task<bool> SendForgotPasswordLinkAsync(string toEmail, string resetLink, int validForMinutes) =>
        SendAsync(toEmail, "Reset your NMB-HLabSys password",
            $@"<p>We received a request to reset your password.</p>
               <p><a href=""{E(resetLink)}"">Click here to choose a new password</a></p>
               <p>The link works once and expires in {validForMinutes} minutes.
                  If you didn't ask for this, you can ignore this email.</p>
               {Signature()}");

    // ------------------------------------------------------------------ supplier orders

    // AC-05-03 / TC-LM-05-01
    public Task<bool> SendOrderPlacedAsync(OrderRow order)
    {
        if (string.IsNullOrWhiteSpace(order.SupplierEmail)) return Task.FromResult(false);

        var rows = string.Concat(order.Items.Select(i =>
            $"<tr><td>{E(i.ConsumableName)}</td><td style=\"text-align:right\">{i.QtyOrdered}</td></tr>"));

        return SendAsync(order.SupplierEmail, $"Purchase order {order.OrderNumber} from {_labName}",
            $@"<p>Dear {E(order.SupplierContact ?? order.SupplierName)},</p>
               <p>Please supply the following items for order <strong>{E(order.OrderNumber)}</strong>
                  (placed {order.DateOrdered:yyyy-MM-dd}):</p>
               <table border=""1"" cellpadding=""6"" cellspacing=""0"">
                 <tr><th>Item</th><th>Quantity</th></tr>
                 {rows}
               </table>
               <p>Please quote the order number on your delivery note.</p>
               {Signature()}");
    }

    // AC-05-07 / TC-LM-05-04
    public Task<bool> SendOrderCancelledAsync(OrderRow order, string reason)
    {
        if (string.IsNullOrWhiteSpace(order.SupplierEmail)) return Task.FromResult(false);

        return SendAsync(order.SupplierEmail, $"Order {order.OrderNumber} cancelled",
            $@"<p>Dear {E(order.SupplierContact ?? order.SupplierName)},</p>
               <p>Order <strong>{E(order.OrderNumber)}</strong> has been cancelled.
                  Please do not deliver any outstanding items on it.</p>
               <p><strong>Reason:</strong> {E(reason)}</p>
               {Signature()}");
    }

    // AC-05-08 / TC-LM-05-05
    public Task<bool> SendOrderItemCancelledAsync(OrderRow order, OrderItemRow item, string reason)
    {
        if (string.IsNullOrWhiteSpace(order.SupplierEmail)) return Task.FromResult(false);

        return SendAsync(order.SupplierEmail, $"Item cancelled on order {order.OrderNumber}",
            $@"<p>Dear {E(order.SupplierContact ?? order.SupplierName)},</p>
               <p>The following item on order <strong>{E(order.OrderNumber)}</strong> has been cancelled:</p>
               <p><strong>{E(item.ConsumableName)}</strong> &times; {item.QtyOrdered}</p>
               <p><strong>Reason:</strong> {E(reason)}</p>
               <p>All other items on the order are unchanged.</p>
               {Signature()}");
    }

    // ------------------------------------------------------------------ plumbing

    private string Signature() => $"<p>Kind regards,<br/>Laboratory Manager<br/>{E(_labName)}</p>";

    /// <summary>HTML-encodes anything a user typed, so a reason like &lt;script&gt; can't run in the email.</summary>
    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    private async Task<bool> SendAsync(string toEmail, string subject, string htmlBody)
    {
        string mode = (_settings.Mode ?? "Pickup").Trim();

        try
        {
            if (mode.Equals("LogOnly", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[LabManager email - not sent] To: {To} | Subject: {Subject}\n{Body}",
                    toEmail, subject, htmlBody);
                return true;
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                Body = $"<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px\">{htmlBody}</div>",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            message.To.Add(new MailAddress(toEmail));

            using var client = new SmtpClient();

            if (mode.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            {
                client.Host = _settings.SmtpHost;
                client.Port = _settings.SmtpPort;
                client.EnableSsl = _settings.EnableSsl;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.Credentials = new NetworkCredential(
                    string.IsNullOrWhiteSpace(_settings.SmtpUsername) ? _settings.SenderEmail : _settings.SmtpUsername,
                    _settings.SmtpPassword);
            }
            else
            {
                // "Pickup": write an .eml file you can open in Outlook / Mail / a text editor
                Directory.CreateDirectory(_pickupDirectory);
                client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                client.PickupDirectoryLocation = _pickupDirectory;
                _logger.LogInformation("[LabManager email saved to {Folder}] To: {To} | Subject: {Subject}",
                    _pickupDirectory, toEmail, subject);
            }

            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LabManager email to {To} failed (subject: {Subject})", toEmail, subject);
            return false;
        }
    }
}
