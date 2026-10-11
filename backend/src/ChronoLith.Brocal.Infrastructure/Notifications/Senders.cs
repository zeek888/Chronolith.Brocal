using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChronoLith.Brocal.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ChronoLith.Brocal.Infrastructure.Notifications;

/// <summary>Platform SMTP account used when a company does not configure its own.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? Host { get; init; }

    public int Port { get; init; } = 587;

    public bool UseSsl { get; init; } = true;

    public string? User { get; init; }

    public string? Password { get; init; }

    public string FromAddress { get; init; } = "no-reply@brocal.local";

    public string FromName { get; init; } = "Brocal";
}

public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    public string ApiBaseUrl { get; init; } = "https://graph.facebook.com";

    public string ApiVersion { get; init; } = "v21.0";
}

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<bool> SendAsync(EmailMessage message, SmtpAccount? account, CancellationToken cancellationToken)
    {
        var platform = options.Value;
        account ??= string.IsNullOrWhiteSpace(platform.Host)
            ? null
            : new SmtpAccount(platform.Host, platform.Port, platform.UseSsl, platform.User, platform.Password, platform.FromAddress, platform.FromName);

        if (account is null)
        {
            logger.LogInformation("Correo simulado (sin SMTP configurado) a {To}: {Subject}", message.To, message.Subject);
            return false;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(account.FromName ?? account.FromAddress, account.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { HtmlBody = message.HtmlBody };
        if (message.Attachment is { } attachment)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        var security = account.UseSsl ? (account.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) : SecureSocketOptions.None;
        await client.ConnectAsync(account.Host, account.Port, security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(account.User))
        {
            await client.AuthenticateAsync(account.User, account.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        return true;
    }
}

/// <summary>
/// WhatsApp Business Cloud API (Meta). Business-initiated messages must use an approved template; the template of the
/// company receives two body parameters: title and detail.
/// </summary>
internal sealed class WhatsAppCloudSender(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<WhatsAppCloudSender> logger) : IWhatsAppSender
{
    public async Task<bool> SendAsync(string to, string title, string body, WhatsAppAccount? account, CancellationToken cancellationToken)
    {
        if (account is null)
        {
            logger.LogInformation("WhatsApp simulado (sin cuenta configurada) a {To}: {Title}", to, title);
            return false;
        }

        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.ApiBaseUrl.TrimEnd('/')}/{settings.ApiVersion}/{account.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                to = to.TrimStart('+'),
                type = "template",
                template = new
                {
                    name = account.Template,
                    language = new { code = account.Language },
                    components = new object[]
                    {
                        new
                        {
                            type = "body",
                            parameters = new object[]
                            {
                                new { type = "text", text = Truncate(title, 60) },
                                new { type = "text", text = Truncate(body.Replace('\n', ' '), 900) }
                            }
                        }
                    }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"WhatsApp respondió {(int)response.StatusCode}: {Truncate(error, 400)}");
        }

        return true;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
