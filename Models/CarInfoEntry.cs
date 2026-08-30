using System.Text.Json.Serialization;

namespace LiveryGallery.Models;

public class CarInfoEntry
{
    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("year")]
    public int? Year { get; set; }
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
