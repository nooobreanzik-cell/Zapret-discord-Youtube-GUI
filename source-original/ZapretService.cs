using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

internal sealed class ServiceState
{
    internal bool Exists, Owned, Running, Automatic;
    internal string Strategy = "zapret";
}
internal static class ZapretService
{
    internal static ServiceState Read(string folder)
    {
        ServiceState state = new ServiceState();
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret"))
        {
            if (key == null) return state;
            state.Exists = true;
            string image = Environment.ExpandEnvironmentVariables((string)key.GetValue("ImagePath", ""));
            Match match = Regex.Match(image, "^(?:\"([^\"]+)\"|(\\S+))");
            string path = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(path))
                state.Owned = Path.GetFullPath(path).Equals(Path.GetFullPath(Path.Combine(folder, "bin", "winws.exe")), StringComparison.OrdinalIgnoreCase);
            state.Automatic = Convert.ToInt32(key.GetValue("Start", 3)) == 2;
            state.Strategy = (string)key.GetValue("zapret-discord-youtube", "zapret");
        }
        using (ServiceController service = new ServiceController("zapret"))
            state.Running = service.Status != ServiceControllerStatus.Stopped;
        return state;
    }
    internal static void EnsureNoForeignService(string folder)
    {
        ServiceState state = Read(folder);
        if (state.Exists && !state.Owned) throw new InvalidOperationException("Служба zapret настроена для другой папки. Останови и удали прежнюю службу через её service.bat, затем повтори запуск. Её настройки не изменены.");
    }
    internal static void Stop(string folder)
    {
        EnsureNoForeignService(folder);
        if (!Read(folder).Exists) return;
        using (ServiceController service = new ServiceController("zapret"))
        {
            if (service.Status != ServiceControllerStatus.Stopped)
            { service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); }
        }
    }
    internal static void SetAutomatic(string folder, bool enabled)
    {
        EnsureNoForeignService(folder);
        if (!Read(folder).Exists) throw new InvalidOperationException("Сначала запусти выбранный альт.");
        using (Process process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), "config zapret start= " + (enabled ? "auto" : "demand")) { UseShellExecute = false, CreateNoWindow = true }))
            if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new IOException("Не удалось изменить автозагрузку службы.");
        if (Read(folder).Automatic != enabled) throw new IOException("Windows не подтвердила изменение автозагрузки.");
    }
}
