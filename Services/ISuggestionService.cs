namespace FSDInfo.Services;

public sealed record SuggestionInput(string? Name, string? Email, string Message);

public interface ISuggestionService
{
    Task<bool> VerifyTurnstileAsync(string token);
    Task SendEmailAsync(SuggestionInput input);
}
