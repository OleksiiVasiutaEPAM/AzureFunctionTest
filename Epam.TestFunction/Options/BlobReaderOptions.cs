namespace Epam.TestFunction.Options;

public sealed record BlobReaderOptions
{
    public long MaxBytes { get; set; } = 1_000_000;
    public string? DefaultContainer { get; set; }
}