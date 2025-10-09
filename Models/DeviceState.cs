namespace systemair_webapi_emulator.Models;

public static class DeviceState
{
    public static readonly string Model = "VSR 500";
    public static readonly string SerialNumber = "SN-287453324";
    public static readonly string HardwareVersion = "2.0";
    
    public static readonly Dictionary<string, Version> InstalledVersions = new()
    {
        { "Mainboard", new Version("1.2.3") },
        { "HMI_software", new Version("2.1.0") },
        { "HMI_resources", new Version("1.0.5") },
        { "IAM-V2", new Version("3.2.1") }
    };
}