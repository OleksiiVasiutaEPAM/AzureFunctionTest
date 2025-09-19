namespace Epam.TestFunction.Options;

public sealed record DialAIOptions()
{
    public string ApiKey { get; set; }
    public string Model { get; set; }
}