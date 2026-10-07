namespace PerceptoX.Presentation.Services;

public interface IPathPickerService
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken);
    Task<string?> PickImageAsync(CancellationToken cancellationToken);
}
