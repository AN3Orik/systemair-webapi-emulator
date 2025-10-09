using System.Net;
using System.Text.Json;
using systemair_webapi_emulator.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<EnvironmentSimulator>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

Console.WriteLine("Systemair SAVECONNECT 2.0 Mock Server is starting...");

app.MapGet("/menu", () =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /menu");
    return Results.Json(new { 
        mac = "AA:BB:CC:DD:EE:FF", 
        mb = "1", 
        cloud = "0", 
        cfg_status = "1", 
        mode = "1" 
    });
});

app.MapGet("/unit_version", () =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] GET /unit_version");
    var response = new Dictionary<string, object>
    {
        { "MB SW version", "1.2.3" },
        { "MB HW version", "2.0" },
        { "MB Model", "VSR 300" },
        { "System Item Number", "12345" },
        { "System Serial Number", "SN-187454321" },
        { "IAM SW version", "3.2.1" }
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

        var registersToWrite = JsonSerializer.Deserialize<Dictionary<string, int>>(decodedQuery);

        foreach (var (key, value) in registersToWrite!)
        {
            if (int.TryParse(key, out var zeroBasedAddress))
            {
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
        }
        return Results.Content("\"OK\"", "application/json");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing /mwrite: {ex.Message}");
        return Results.BadRequest("Invalid query format.");
    }
});

Console.WriteLine("Listening on http://*:80");
app.Run("http://*:80");