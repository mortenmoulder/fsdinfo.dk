using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FSDInfo.Models;

namespace FSDInfo.Services;

public class VehicleService : IVehicleService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VehicleService> _logger;

    // Standard VIN year code → calendar year (Tesla uses same encoding as SAE J826)
    // Letters I, O, Q, U, Z are not used. 10-year cycle repeats after Y.
    private static readonly Dictionary<char, int> VinYearMap = new()
    {
        ['A'] = 2010, ['B'] = 2011, ['C'] = 2012, ['D'] = 2013,
        ['E'] = 2014, ['F'] = 2015, ['G'] = 2016, ['H'] = 2017,
        ['J'] = 2018, ['K'] = 2019, ['L'] = 2020, ['M'] = 2021,
        ['N'] = 2022, ['P'] = 2023, ['R'] = 2024, ['S'] = 2025,
        ['T'] = 2026, ['V'] = 2027, ['W'] = 2028, ['X'] = 2029,
        ['Y'] = 2030
    };

    // Danish standard plate: 2 letters (A-Z) followed by exactly 5 digits
    private static readonly Regex DanishPlateRegex = new(@"^[A-Z]{2}\d{5}$", RegexOptions.Compiled);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public VehicleService(HttpClient httpClient, ILogger<VehicleService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<VehicleCheckResult> CheckLicensePlateAsync(string licensePlate)
    {
        // Plate has already been validated client-side; validate again server-side as defence in depth
        if (!DanishPlateRegex.IsMatch(licensePlate))
        {
            _logger.LogWarning("Rejected plate with invalid format: {Plate}", licensePlate);
            return new VehicleCheckResult { HardwareResult = VehicleHardwareResult.ApiError };
        }

        try
        {
            var response = await _httpClient.GetAsync($"api/vehicles/reg/{licensePlate}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("No vehicle found for plate {Plate}", licensePlate);
                return new VehicleCheckResult { HardwareResult = VehicleHardwareResult.Unknown };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Vehicle API returned {StatusCode} for plate {Plate}", response.StatusCode, licensePlate);
                return new VehicleCheckResult { HardwareResult = VehicleHardwareResult.ApiError };
            }

            var vehicle = await response.Content.ReadFromJsonAsync<VehicleApiResponse>(JsonOptions);
            if (vehicle == null)
                return new VehicleCheckResult { HardwareResult = VehicleHardwareResult.Unknown };

            var hwResult = IsTesla(vehicle.MaerkeNavn)
                ? DetermineHardware(vehicle)
                : VehicleHardwareResult.NotTesla;

            return new VehicleCheckResult
            {
                HardwareResult  = hwResult,
                ModelNavn       = vehicle.ModelNavn,
                VariantNavn     = vehicle.VariantNavn,
                ModelAar        = vehicle.ModelAar,
                FarveNavn       = vehicle.FarveNavn,
                DrivkraftNavn   = vehicle.DrivkraftNavn
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach vehicle API for plate {Plate}", licensePlate);
            return new VehicleCheckResult { HardwareResult = VehicleHardwareResult.ApiError };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking plate {Plate}", licensePlate);
            return new VehicleCheckResult { HardwareResult = VehicleHardwareResult.ApiError };
        }
    }

    private static bool IsTesla(string? make)
        => string.Equals(make?.Trim(), "tesla", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the model-year character from VIN position 10 (index 9) and maps it to a calendar year.
    /// Returns 0 if the VIN is too short or the character is not in the map.
    /// </summary>
    private static int GetVinYear(string? vin)
        => vin?.Length >= 10 && VinYearMap.TryGetValue(vin[9], out var y) ? y : 0;

    private static VehicleHardwareResult DetermineHardware(VehicleApiResponse vehicle)
    {
        var model   = vehicle.ModelNavn?.ToLowerInvariant() ?? string.Empty;
        int modelAar = vehicle.ModelAar;
        int vinYear  = GetVinYear(vehicle.StelNummer);

        bool isModel3      = model.Contains("3");
        bool isModelY      = model.Contains("y");
        bool isCybertruck  = model.Contains("cybertruck");

        // ── Cybertruck ────────────────────────────────────────────────────────
        // All Cybertrucks ship with HW4.
        if (isCybertruck) return VehicleHardwareResult.HW4;

        // ── Model 3 Highland ─────────────────────────────────────────────────
        // Highland (Gen 2 facelift) launched globally in Q4 2023.
        // By VIN year R (2024) the Highland was the ONLY Model 3 in production.
        // → any Model 3 with VIN year ≥ 2024 OR model_aar ≥ 2024 is a Highland → HW4.
        if (isModel3)
        {
            bool isHighland = vinYear >= 2024 || modelAar >= 2024;
            if (isHighland) return VehicleHardwareResult.HW4;
            if (modelAar < 2023 || (vinYear > 0 && vinYear < 2023))
                return VehicleHardwareResult.HW3;      // Definitely pre-Highland
            return VehicleHardwareResult.Unknown;      // 2023 Model 3 – could be early pre-Highland
        }

        // ── Model Y Juniper ───────────────────────────────────────────────────
        // Juniper launched in EU/DK in January 2025 (VIN year S = 2025).
        // A 2024 (VIN year R) Model Y from Denmark is pre-Juniper.
        // → VIN year ≥ 2025 OR model_aar ≥ 2025 = Juniper → HW4.
        if (isModelY)
        {
            bool isJuniper = vinYear >= 2025 || modelAar >= 2025;
            if (isJuniper) return VehicleHardwareResult.HW4;
            if (vinYear == 2024 || modelAar == 2024)
                return VehicleHardwareResult.ProbablyHW3;  // Pre-Juniper 2024 – not a Juniper body but could have HW4 chip
            return VehicleHardwareResult.HW3;              // Definitely pre-Juniper and pre-HW4 update (pre-2024)
        }

        // ── Other Tesla models (Model S, Model X) ─────────────────────────────
        if (modelAar >= 2025) return VehicleHardwareResult.HW4;
        if (modelAar == 2024) return VehicleHardwareResult.Unknown;
        return VehicleHardwareResult.HW3;
    }
}

