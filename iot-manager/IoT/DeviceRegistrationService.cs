using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;

public class DeviceRegistrationService
{
    private readonly RegistryManager _registryManager;
    private readonly ILogger<DeviceRegistrationService> _logger;

    public DeviceRegistrationService(RegistryManager registryManager, ILogger<DeviceRegistrationService> logger)
    {
        _registryManager = registryManager;
        _logger = logger;
    }

    public async Task<string> RegisterDeviceAsync(string deviceId, string iotHubHostName)
    {
        Device device;

        try
        {
            device = await _registryManager.AddDeviceAsync(new Device(deviceId));
            _logger.LogInformation("Device {deviceId} registered successfully.", deviceId);
        }
        catch (DeviceAlreadyExistsException)
        {
            device = await _registryManager.GetDeviceAsync(deviceId);
            _logger.LogInformation("Device {deviceId} already exists.", deviceId);
        }

        string primaryKey = device.Authentication.SymmetricKey.PrimaryKey;
        string deviceConnectionString = $"HostName={iotHubHostName};DeviceId={deviceId};SharedAccessKey={primaryKey}";

        return deviceConnectionString;
    }

    public async Task DeregisterDeviceAsync(string deviceId)
    {
        try
        {
            var device = await _registryManager.GetDeviceAsync(deviceId);
            _logger.LogInformation("Removing {deviceId} from the registry", deviceId);
            await _registryManager.RemoveDeviceAsync(device);
        }
        catch (DeviceNotFoundException ex)
        {
            _logger.LogWarning(ex, "Device {deviceId} does not exist.", deviceId);
        }
    }
}