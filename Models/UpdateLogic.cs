namespace systemair_webapi_emulator.Models;

public static class UpdateLogic
{
    private const int MAX_RPM = 5000;

    private const int CROWDED_MODE_PERCENTAGE = 90;
    private const int REFRESH_MODE_PERCENTAGE = 100;

    public static void ApplyLogic(int registerAddress, int newValue)
    {
        Console.WriteLine($"    -> Applying logic for write to register {registerAddress} with value {newValue}");

        switch (registerAddress)
        {
            case 1162: // REG_USERMODE_HMI_CHANGE_REQUEST
                var newModeStatus = newValue - 1;
                UpdateRegister(1161, newModeStatus);
                ApplyFanSpeedsForMode(newModeStatus);
                break;

            case 1131: // REG_USERMODE_MANUAL_AIRFLOW_LEVEL_SAF
                if (ModbusDataStore.Registers.TryGetValue(1161, out var modeReg) && modeReg.Value == 1)
                {
                    ApplyFanSpeedsForMode(1);
                }
                else if (ModbusDataStore.Registers.TryGetValue(1161, out modeReg) && modeReg.Value != 1)
                {
                    Console.WriteLine("    -> Fan speed changed while not in Manual mode. Simulating switch to Manual mode.");
                    UpdateRegister(1161, 1);
                    ApplyFanSpeedsForMode(1);
                }
                break;
            
            case 1105: // REG_USERMODE_CROWDED_TIME
                if (newValue > 0) { UpdateRegister(1161, 2); ApplyFanSpeedsForMode(2); }
                break;
            case 1104: // REG_USERMODE_REFRESH_TIME
                if (newValue > 0) { UpdateRegister(1161, 3); ApplyFanSpeedsForMode(3); }
                break;
            case 1103: // REG_USERMODE_FIREPLACE_TIME
                if (newValue > 0) { UpdateRegister(1161, 4); ApplyFanSpeedsForMode(4); }
                break;
            case 1102: // REG_USERMODE_AWAY_TIME
                if (newValue > 0) { UpdateRegister(1161, 5); ApplyFanSpeedsForMode(5); }
                break;
            case 1101: // REG_USERMODE_HOLIDAY_TIME
                if (newValue > 0) { UpdateRegister(1161, 6); ApplyFanSpeedsForMode(6); }
                break;

            case 2001: // REG_TC_SP
                SimulateHeatingCoolingDemand();
                break;

            case 7002:
            case 7003:
                if (ModbusDataStore.Registers.TryGetValue(7001, out var periodReg))
                {
                    long filterPeriodMonths = periodReg.Value;
                    long remainingSeconds = filterPeriodMonths * 30 * 24 * 3600;
                    int lower = (int)(remainingSeconds & 0xFFFF);
                    int upper = (int)(remainingSeconds >> 16);
                    UpdateRegister(7005, lower);
                    UpdateRegister(7006, upper);
                    Console.WriteLine($"    -> Filter timer reset. Remaining time set to {remainingSeconds} seconds.");
                }
                break;

            case 4101: UpdateRegister(3102, newValue); break;
            case 2505: UpdateRegister(2506, newValue); break;
            case 2134: UpdateRegister(3106, newValue); break;
        }
    }

    private static void ApplyFanSpeedsForMode(int mode)
    {
        var regulationUnit = ModbusDataStore.Registers.GetValueOrDefault(1274, new ModbusRegister(0, 0, 4)).Value;
        Console.WriteLine($"    -> Applying fan speeds for mode {mode}. Fan regulation unit is: {regulationUnit} (0=%, 1=RPM, ...)");

        var modeToFanLevelSettingsRegs = new Dictionary<int, (int safReg, int eafReg)>
        {
            { 1, (1131, 1131) }, // Manual
            // Crowded (2) and Refresh (3) will be handled separately
            { 4, (1139, 1140) }, // Fireplace
            { 5, (1141, 1142) }, // Away
            { 6, (1143, 1144) }, // Holiday
            { 7, (1145, 1146) }, // Cooker Hood
            { 8, (1147, 1148) }, // Vacuum Cleaner
            { 9, (1171, 1172) }, // CDI 1
            { 10, (1173, 1174) },// CDI 2
            { 11, (1175, 1176) },// CDI 3
            { 12, (1177, 1178) } // Pressure Guard
        };

        int safPercentage = 0;
        int eafPercentage = 0;

        // Handle special modes first
        switch (mode)
        {
            case 0: // Auto mode
                safPercentage = GetPercentageForLevel(3, isSupplyFan: true);
                eafPercentage = GetPercentageForLevel(3, isSupplyFan: false);
                break;
            
            case 2: // Crowded mode
                Console.WriteLine($"    -> Crowded mode activated. Overriding fan speed to {CROWDED_MODE_PERCENTAGE}%.");
                safPercentage = CROWDED_MODE_PERCENTAGE;
                eafPercentage = CROWDED_MODE_PERCENTAGE;
                break;

            case 3: // Refresh mode
                Console.WriteLine($"    -> Refresh mode activated. Overriding fan speed to {REFRESH_MODE_PERCENTAGE}%.");
                safPercentage = REFRESH_MODE_PERCENTAGE;
                eafPercentage = REFRESH_MODE_PERCENTAGE;
                break;

            default: // Handle all other modes using their configured levels
                if (modeToFanLevelSettingsRegs.TryGetValue(mode, out var regs))
                {
                    int safLevel = ModbusDataStore.Registers.GetValueOrDefault(regs.safReg, new ModbusRegister(0,0,0)).Value;
                    int eafLevel = ModbusDataStore.Registers.GetValueOrDefault(regs.eafReg, new ModbusRegister(0,0,0)).Value;
                    safPercentage = GetPercentageForLevel(safLevel, isSupplyFan: true);
                    eafPercentage = GetPercentageForLevel(eafLevel, isSupplyFan: false);
                }
                break;
        }

        UpdateRegister(14001, safPercentage); // REG_OUTPUT_SAF (%)
        UpdateRegister(14002, eafPercentage); // REG_OUTPUT_EAF (%)
        
        bool fansRunning = (safPercentage > 0 || eafPercentage > 0);
        UpdateRegister(1351, fansRunning ? 1 : 0);

        UpdateFanRPMs();
    }

    private static int GetPercentageForLevel(int level, bool isSupplyFan)
    {
        int regAddress = level switch
        {
            0 => 0,
            1 => isSupplyFan ? 1401 : 1402, // Minimum
            2 => isSupplyFan ? 1403 : 1404, // Low
            3 => isSupplyFan ? 1405 : 1406, // Normal
            4 => isSupplyFan ? 1407 : 1408, // High
            5 => isSupplyFan ? 1409 : 1410, // Maximum
            _ => 0
        };
        if (regAddress == 0) return 0;
        return ModbusDataStore.Registers.GetValueOrDefault(regAddress, new ModbusRegister(0,0,0)).Value;
    }

    public static void UpdateFanRPMs()
    {
        var safPercentage = ModbusDataStore.Registers.GetValueOrDefault(14001, new ModbusRegister(0,0,0)).Value;
        var eafPercentage = ModbusDataStore.Registers.GetValueOrDefault(14002, new ModbusRegister(0,0,0)).Value;

        int safRpm = (int)Math.Round((safPercentage / 100.0) * MAX_RPM);
        int eafRpm = (int)Math.Round((eafPercentage / 100.0) * MAX_RPM);

        UpdateRegister(12401, safRpm);
        UpdateRegister(12402, eafRpm);
    }

    private static void SimulateHeatingCoolingDemand()
    {
        var setpoint = ModbusDataStore.Registers[2001].Value / 10.0;
        var currentTemp = ModbusDataStore.Registers[12103].Value / 10.0;
        var diff = setpoint - currentTemp;

        if (diff > 0.5) {
            int heatDemand = Math.Min(100, (int)(diff * 20));
            UpdateRegister(2114, heatDemand); UpdateRegister(2311, 0);
            UpdateRegister(14381, 1); UpdateRegister(3103, 1); UpdateRegister(3101, 0);
        } else if (diff < -0.5) {
            int coolDemand = Math.Min(100, (int)(-diff * 20));
            UpdateRegister(2114, 0); UpdateRegister(2311, coolDemand);
            UpdateRegister(14381, 0); UpdateRegister(3103, 0); UpdateRegister(3101, 1);
        } else {
            UpdateRegister(2114, 0); UpdateRegister(2311, 0);
            UpdateRegister(14381, 0); UpdateRegister(3103, 0); UpdateRegister(3101, 0);
        }
    }

    public static void UpdateRegister(int address, int value)
    {
        if (ModbusDataStore.Registers.TryGetValue(address, out var register))
        {
            register.Value = value;
            Console.WriteLine($"    -> Internal update: Register {address} set to {value}");
        }
        else
        {
            Console.WriteLine($"    -> Warning: Attempted to internally update non-existent register {address}.");
        }
    }
}