namespace systemair_webapi_emulator.Models;

public class EnvironmentSimulator : IHostedService, IDisposable
{
    private Timer? _timer;
    private readonly Random _random = new();

    // Initial simulation values
    private double _outsideAirTemp = 10.0;
    private double _extractAirTemp = 21.5;
    private double _supplyAirTemp = 18.0;
    private double _relativeHumidity = 45.0;

    // Variable for simulating daily temperature cycle
    private double _dailyTempCycle = 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Environment Simulator is starting.");
        _timer = new Timer(UpdateSensorValues, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    private void UpdateSensorValues(object? state)
    {
        // Get the current state of the device from the DataStore ---
        var targetTemp = ModbusDataStore.Registers.GetValueOrDefault(2001, new ModbusRegister(220, 0, 0)).Value / 10.0;
        var heaterDemand = ModbusDataStore.Registers.GetValueOrDefault(2114, new ModbusRegister(0, 0, 0)).Value; // 0-100%
        var coolerDemand = ModbusDataStore.Registers.GetValueOrDefault(2311, new ModbusRegister(0, 0, 0)).Value; // 0-100%
        var heatRecoveryDemand = ModbusDataStore.Registers.GetValueOrDefault(2141, new ModbusRegister(0, 0, 0)).Value; // 0-100%
        var fansRunning = ModbusDataStore.Registers.GetValueOrDefault(1351, new ModbusRegister(0, 0, 0)).Value == 1;

        // Simulate physical processes

        // Simulate Outside Air Temperature (OAT) with a slow daily cycle
        _dailyTempCycle += 0.01;
        _outsideAirTemp = 10.0 + Math.Sin(_dailyTempCycle) * 5.0 + (_random.NextDouble() - 0.5) * 0.1;

        // Simulate indoor / Extract Air Temperature (EAT)
        // It slowly moves towards the target temperature when the ventilation is running.
        if (fansRunning)
        {
            _extractAirTemp += (targetTemp - _extractAirTemp) * 0.01; // Slow adjustment towards setpoint
        }
        _extractAirTemp += (_random.NextDouble() - 0.5) * 0.05; // Add a little noise

        // Simulate Supply Air Temperature (SAT) - the most complex part
        double heatRecoveryEffect = 0;
        if (fansRunning && heatRecoveryDemand > 0)
        {
            // Assume ~80% heat exchanger efficiency
            heatRecoveryEffect = (_extractAirTemp - _outsideAirTemp) * 0.8 * (heatRecoveryDemand / 100.0);
        }
        
        double baseSupplyTemp = _outsideAirTemp + heatRecoveryEffect;
        double heaterEffect = (heaterDemand / 100.0) * 0.2; // A 100% demand adds up to 20 degrees over 5 seconds
        double coolerEffect = (coolerDemand / 100.0) * 0.2; // A 100% demand removes up to 20 degrees over 5 seconds

        _supplyAirTemp = baseSupplyTemp + heaterEffect - coolerEffect;
        _supplyAirTemp += (_random.NextDouble() - 0.5) * 0.1; // Add noise

        // Simulate Relative Humidity (RH)
        if (fansRunning)
        {
            _relativeHumidity -= 0.1; // Ventilation dries the air
        }
        else
        {
            _relativeHumidity += 0.05; // Humidity builds up from occupancy
        }
        _relativeHumidity = Math.Clamp(_relativeHumidity, 30.0, 70.0); // Keep it within a reasonable range
        _relativeHumidity += (_random.NextDouble() - 0.5) * 0.2; // Add noise

        // Update the registers in the DataStore
        // Values are multiplied by 10 as they are stored as integers with one decimal place in Modbus.
        UpdateLogic.UpdateRegister(12102, (int)(_outsideAirTemp * 10)); // REG_SENSOR_OAT
        UpdateLogic.UpdateRegister(12103, (int)(_supplyAirTemp * 10));  // REG_SENSOR_SAT
        UpdateLogic.UpdateRegister(12544, (int)(_extractAirTemp * 10)); // REG_SENSOR_PDM_EAT_VALUE
        UpdateLogic.UpdateRegister(12136, (int)_relativeHumidity);      // REG_SENSOR_RHS_PDM

        Console.WriteLine($"[SIMULATOR] OAT: {_outsideAirTemp:F1}°C, SAT: {_supplyAirTemp:F1}°C, EAT: {_extractAirTemp:F1}°C, RH: {_relativeHumidity:F1}%");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Environment Simulator is stopping.");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}