// Calls five SCE kernel functions from libkernel_sys to
// read the console model name, serial number, SoC temperature, CPU temperature, and CPU
// frequency, then outputs the results via klog.

using System.Runtime.InteropServices;

namespace SampleApp;

internal static unsafe partial class Program
{
    [LibraryImport("libkernel_sys", EntryPoint = "sceKernelGetHwModelName")]
    private static partial int GetHwModelName(byte* buffer);

    [LibraryImport("libkernel_sys", EntryPoint = "sceKernelGetHwSerialNumber")]
    private static partial int GetHwSerialNumber(byte* buffer);

    [LibraryImport("libkernel_sys", EntryPoint = "sceKernelGetCpuFrequency")]
    private static partial long GetCpuFrequency();

    [LibraryImport("libkernel_sys", EntryPoint = "sceKernelGetCpuTemperature")]
    private static partial int GetCpuTemperature(int* temperature);

    [LibraryImport("libkernel_sys", EntryPoint = "sceKernelGetSocSensorTemperature")]
    private static partial int GetSocSensorTemperature(int sensor, int* temperature);

    [LibraryImport("libScePosix", EntryPoint = "__prospero_klog")]
    private static partial void Klog(byte* message);

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    public static int Main(void* args)
    {
        byte* s = stackalloc byte[1000];
        s[0] = 0;
        int temp = 0;

        if (GetHwModelName(s) == 0)
        {
            LogField("Model:\t\t "u8, s);
        }

        s[0] = 0;
        if (GetHwSerialNumber(s) == 0)
        {
            LogField("S/N:\t\t "u8, s);
        }

        if (GetSocSensorTemperature(0, &temp) == 0)
        {
            LogInt("SoC temp:\t "u8, temp, " C\n"u8);
        }

        if (GetCpuTemperature(&temp) == 0)
        {
            LogInt("CPU temp:\t "u8, temp, " C\n"u8);
        }

        long freqHz = GetCpuFrequency();
        long freqMhz = freqHz / (1000 * 1000);
        LogInt("CPU freq:\t "u8, (int)freqMhz, " MHz\n"u8);

        return 0;
    }

    private static void LogField(System.ReadOnlySpan<byte> prefix, byte* value)
    {
        byte* line = stackalloc byte[1100];
        int pos = 0;
        for (int i = 0; i < prefix.Length; i++)
            line[pos++] = prefix[i];
        for (int i = 0; value[i] != 0 && pos < 1090; i++)
            line[pos++] = value[i];
        line[pos++] = (byte)'\n';
        line[pos] = 0;
        Klog(line);
    }

    private static void LogInt(System.ReadOnlySpan<byte> prefix, int value, System.ReadOnlySpan<byte> suffix)
    {
        byte* line = stackalloc byte[256];
        int pos = 0;
        for (int i = 0; i < prefix.Length; i++)
            line[pos++] = prefix[i];

        // Convert int to decimal string.
        if (value < 0) { line[pos++] = (byte)'-'; value = -value; }
        byte* digits = stackalloc byte[20];
        int d = 0;
        if (value == 0) { digits[d++] = (byte)'0'; }
        else { while (value > 0) { digits[d++] = (byte)('0' + value % 10); value /= 10; } }
        for (int i = d - 1; i >= 0; i--)
            line[pos++] = digits[i];

        for (int i = 0; i < suffix.Length; i++)
            line[pos++] = suffix[i];
        line[pos] = 0;
        Klog(line);
    }
}
