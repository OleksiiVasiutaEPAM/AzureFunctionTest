namespace Epam.TestFunction.Options;

public sealed record OpenAIOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";

    // Для Azure OpenAI (необязательно)
    public string? AzureEndpoint { get; set; }
    public string? AzureApiKey { get; set; }
    public string? AzureDeployment { get; set; }
}