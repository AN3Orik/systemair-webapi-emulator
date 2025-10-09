namespace systemair_webapi_emulator.Models;

public static class UpdateSimulator
{
    public static List<string> UploadedFiles { get; } = [];
    public static bool IsUpdating { get; set; } = false;
    public static int UpdateProgress { get; set; } = 0;
}