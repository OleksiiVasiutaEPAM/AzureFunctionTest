using System.Text.Json.Serialization;

namespace Epam.TestFunction.Models;

public record Input([property: JsonPropertyName("prompt")] string Prompt, double? Temperature);