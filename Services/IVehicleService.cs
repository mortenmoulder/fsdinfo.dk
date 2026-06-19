using FSDInfo.Models;

namespace FSDInfo.Services;

public interface IVehicleService
{
    Task<VehicleCheckResult> CheckLicensePlateAsync(string licensePlate);
}
