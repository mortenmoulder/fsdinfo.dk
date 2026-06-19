using System.Text.Json.Serialization;

namespace FSDInfo.Models;

public enum VehicleHardwareResult
{
    HW4,
    ProbablyHW3,  // Likely HW3 but not confirmed – user should verify in car
    HW3,
    Unknown,      // Genuinely uncertain (e.g., 2023 Model 3 could be either)
    NotTesla,
    ApiError
}

public class VehicleCheckResult
{
    public required VehicleHardwareResult HardwareResult { get; init; }
    public string? ModelNavn { get; init; }
    public string? VariantNavn { get; init; }
    public int ModelAar { get; init; }
    public string? FarveNavn { get; init; }
    public string? DrivkraftNavn { get; init; }
    public bool HasVehicleInfo => ModelNavn is not null;
}

public class VehicleApiResponse
{
    [JsonPropertyName("maerke_navn")]
    public string? MaerkeNavn { get; set; }

    [JsonPropertyName("model_navn")]
    public string? ModelNavn { get; set; }

    [JsonPropertyName("variant_navn")]
    public string? VariantNavn { get; set; }

    [JsonPropertyName("model_aar")]
    public int ModelAar { get; set; }

    [JsonPropertyName("farve_navn")]
    public string? FarveNavn { get; set; }

    [JsonPropertyName("drivkraft_navn")]
    public string? DrivkraftNavn { get; set; }

    /// <summary>VIN / stelnummer – used to extract model year code at position 10 (index 9).</summary>
    [JsonPropertyName("stel_nummer")]
    public string? StelNummer { get; set; }
}
