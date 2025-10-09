using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Json;
using systemair_webapi_emulator.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<EnvironmentSimulator>();
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null; 
});


var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

Console.WriteLine("Systemair SAVECONNECT 2.0 Mock Server is starting...");

app.MapGet("/menu", () => Results.Json(new { mac = "AA:BB:CC:DD:EE:FF", mb = "1", cloud = "0", cfg_status = "1", mode = "1" }));

app.MapGet("/unit_version", () =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /unit_version");
    var response = new Dictionary<string, object>
    {
        { "MB SW version", DeviceState.InstalledVersions["Mainboard"].ToString() },
        { "MB HW version", DeviceState.HardwareVersion },
        { "MB Model", DeviceState.Model },
        { "System Item Number", "12345" },
        { "System Serial Number", DeviceState.SerialNumber },
        { "IAM SW version", DeviceState.InstalledVersions["IAM-V2"].ToString() }
    };
    return Results.Json(response);
});

app.MapGet("/mread", (HttpContext context) =>
{
    var query = context.Request.QueryString.Value?.TrimStart('?');
    if (string.IsNullOrEmpty(query)) return Results.BadRequest("Query is empty.");

    try
    {
        var decodedQuery = WebUtility.UrlDecode(query);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /mread with query: {decodedQuery}");

        var requestedRegisters = JsonSerializer.Deserialize<Dictionary<string, int>>(decodedQuery);
        var response = new Dictionary<string, int>();

        foreach (var key in requestedRegisters!.Keys)
        {
            if (int.TryParse(key, out var zeroBasedAddress))
            {
                int oneBasedAddress = zeroBasedAddress + 1;
                var value = ModbusDataStore.Registers.GetValueOrDefault(oneBasedAddress, new ModbusRegister(0, 0, 0, true)).Value;
                response[key] = value;
            }
        }
        return Results.Json(response);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing /mread: {ex.Message}");
        return Results.BadRequest("Invalid query format.");
    }
});

app.MapGet("/mwrite", (HttpContext context) =>
{
    var query = context.Request.QueryString.Value?.TrimStart('?');
    if (string.IsNullOrEmpty(query)) return Results.BadRequest("Query is empty.");

    try
    {
        var decodedQuery = WebUtility.UrlDecode(query);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /mwrite with query: {decodedQuery}");
        
        var registersToWrite = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(decodedQuery);

        foreach (var (key, jsonElement) in registersToWrite!)
        {
            if (!int.TryParse(key, out var zeroBasedAddress))
            {
                Console.WriteLine($"    -> Skipping non-register key: '{key}'");
                continue;
            }
            
            if (!jsonElement.TryGetInt32(out var value))
            {
                Console.WriteLine($"    -> Skipping register '{key}' because its value is not a valid integer (e.g., it might be null).");
                continue;
            }

            int oneBasedAddress = zeroBasedAddress + 1;

            if (ModbusDataStore.Registers.TryGetValue(oneBasedAddress, out var register))
            {
                if (register.IsReadOnly)
                {
                    Console.WriteLine($"    -> Attempted to write to read-only register {oneBasedAddress}. Ignoring.");
                    continue;
                }

                var clampedValue = Math.Clamp(value, register.MinValue, register.MaxValue);
                if (clampedValue != value)
                {
                    Console.WriteLine($"    -> Value {value} for register {oneBasedAddress} was clamped to {clampedValue}.");
                }
                
                register.Value = clampedValue;
                Console.WriteLine($"    -> Wrote value {clampedValue} to register {oneBasedAddress}");

                UpdateLogic.ApplyLogic(oneBasedAddress, clampedValue);
            }
            else
            {
                 Console.WriteLine($"    -> Warning: Register {oneBasedAddress} not found in DataStore. Writing anyway.");
                 ModbusDataStore.Registers[oneBasedAddress] = new ModbusRegister(value, int.MinValue, int.MaxValue);
            }
        }
        return Results.Text("OK");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing /mwrite: {ex.Message}");
        return Results.BadRequest("Invalid query format.");
    }
});

app.MapGet("/file_ver", () =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /file_ver");
    
    var installedVersions = DeviceState.InstalledVersions;
    bool canUpdate = false;

    foreach (var filename in UpdateSimulator.UploadedFiles)
    {
        var match = Regex.Match(filename, @"Bifrost(?:_release_|-)(?<type>.*?)(?:_software_|_)?(?<version>\d+\.\d+\.\d+)\.bin");
        if (match.Success)
        {
            var fileType = match.Groups["type"].Value;
            var fileVersion = new Version(match.Groups["version"].Value);

            if (installedVersions.TryGetValue(fileType, out var installedVersion) && fileVersion > installedVersion)
            {
                Console.WriteLine($"    -> Found new firmware: {fileType} version {fileVersion} (installed: {installedVersion}). Update is possible.");
                canUpdate = true;
                break;
            }
        }
    }

    var response = new
    {
        MB_flash = new Dictionary<string, object>
        {
            { "MB_file ver", installedVersions["Mainboard"].ToString() },
            { "HMI_file ver", installedVersions["HMI_software"].ToString() },
            { "HMI_resources_file ver", installedVersions["HMI_resources"].ToString() },
            { "MB_l486 ver", "N/A" }, { "HMI_STM ver", "N/A" }, { "MB_G487 ver", "N/A" }
        },
        HMIs = new List<object>(),
        IAM_flash = new Dictionary<string, object>
        {
            { "MB_file ver", installedVersions["IAM-V2"].ToString() },
            { "HMI_file ver", "N/A" }, { "HMI_resources_file ver", "N/A" },
            { "MB_l486 ver", "N/A" }, { "HMI_STM ver", "N/A" }, { "MB_G487 ver", "N/A" }
        },
        Start_update = canUpdate.ToString().ToLower()
    };
    
    return Results.Json(response);
});

app.MapPost("/upload/{filename}", (string filename) => {
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] POST /upload/{filename}");
    if (!UpdateSimulator.UploadedFiles.Contains(filename))
    {
        UpdateSimulator.UploadedFiles.Add(filename);
    }
    return Results.Json(new { fw_list = UpdateSimulator.UploadedFiles });
});

app.MapGet("/fw_list", () => {
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /fw_list");
    return Results.Json(UpdateSimulator.UploadedFiles);
});

app.MapGet("/start_upd", () => {
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /start_upd");
    if (!UpdateSimulator.IsUpdating)
    {
        UpdateSimulator.IsUpdating = true;
        UpdateSimulator.UpdateProgress = 0;
        
        _ = Task.Run(async () => {
            while (UpdateSimulator.UpdateProgress < 100)
            {
                await Task.Delay(500);
                UpdateSimulator.UpdateProgress += 10;
                Console.WriteLine($"[UPDATESIM] Progress: {UpdateSimulator.UpdateProgress}%");
            }
            await Task.Delay(1000);
            UpdateSimulator.IsUpdating = false;
            Console.WriteLine("[UPDATESIM] Update finished.");
        });
    }
    return Results.Text("OK");
});

app.MapGet("/status_upd", () => {
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /status_upd");
    if (UpdateSimulator.IsUpdating)
    {
        return Results.Json(new {
            status = 1, // 1 = IN PROGRESS
            file = "30301",
            percentage = UpdateSimulator.UpdateProgress
        });
    }
    
    return Results.Json(new {
        status = 0, // 0 = IDLE
        file = "0",
        percentage = 100
    });
});


Console.WriteLine("Listening on http://*:80");
app.Run("http://*:80");