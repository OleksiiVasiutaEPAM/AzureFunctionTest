namespace Epam.TestFunction.Services.Interfaces;

public interface IBlobFileService
{
    Task<(string? Text, string ContentType, long Length, string FileName)> ReadTextAsync(
        string container, string blobName, CancellationToken ct = default);

    Task<(Stream Content, string ContentType, long Length, string FileName)> OpenReadAsync(
        string container, string blobName, CancellationToken ct = default);
}