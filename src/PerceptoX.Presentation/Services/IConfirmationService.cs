namespace PerceptoX.Presentation.Services;

public interface IConfirmationService
{
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken);
}
