namespace systemair_webapi_emulator.Models;

public class ModbusRegister(int initialValue, int minValue, int maxValue, bool isReadOnly = false)
{
    public int Value { get; set; } = initialValue;
    public int MinValue { get; } = minValue;
    public int MaxValue { get; } = maxValue;
    public bool IsReadOnly { get; } = isReadOnly;
}