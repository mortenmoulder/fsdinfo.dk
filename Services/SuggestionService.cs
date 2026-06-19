using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace FSDInfo.Services;

public sealed class SuggestionService : ISuggestionService
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<SuggestionService> _logger;

    public SuggestionService(IHttpClientFactory http, IConfiguration config, ILogger<SuggestionService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> VerifyTurnstileAsync(string token)
    {
        var secret = _config["Turnstile:SecretKey"];
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogWarning("Turnstile:SecretKey is not configured");
            return false;
        }

        try
        {
            using var client = _http.CreateClient();
            var response = await client.PostAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = secret,
                    ["response"] = token
                }));

            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("success").GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Turnstile verification failed");
            return false;
        }
    }

    public async Task SendEmailAsync(SuggestionInput input)
    {
        var smtpHost = _config["Email:SmtpHost"] ?? "localhost";
        var smtpPort = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 25;
        var from = _config["Email:From"]!;
        var to = _config["Email:To"]!;
        var smtpUser = _config["Email:SmtpUser"];
        var smtpPass = _config["Email:SmtpPassword"];

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = "Nyt forslag – FSDInfo.dk";
        message.Body = new TextPart("plain")
        {
            Text = $"""
                Navn:   {input.Name ?? "(ikke angivet)"}
                E-mail: {input.Email ?? "(ikke angivet)"}

                {input.Message}
                """
        };

        using var smtp = new SmtpClient();
        try
        {
            await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.Auto);
            if (!string.IsNullOrEmpty(smtpUser))
                await smtp.AuthenticateAsync(smtpUser, smtpPass ?? string.Empty);
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);
            _logger.LogInformation("Suggestion email sent to {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send suggestion email. Host={Host} Port={Port} From={From} To={To}",
                smtpHost, smtpPort, from, to);
            throw;
        }
    }
}
