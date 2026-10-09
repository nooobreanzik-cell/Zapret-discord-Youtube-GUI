using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Maintenance
{
    internal static void BeforeUninstall()
    {
        int ownPid = Process.GetCurrentProcess().Id;
        foreach (Process process in Process.GetProcessesByName("ZapretGui"))
            using (process) if (process.Id != ownPid) throw new InvalidOperationException("Сначала закрой все окна Zapret GUI.");
        string engine = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zapret") + Path.DirectorySeparatorChar;
        RemoveOwnedService("zapret", engine, false);
        foreach (Process process in Process.GetProcessesByName("winws"))
            using (process)
            {
                string path;
                try { path = process.MainModule.FileName; }
                catch { throw new InvalidOperationException("Не удалось проверить работающий winws. Останови Zapret перед удалением."); }
                if (Path.GetFullPath(path).StartsWith(engine, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Встроенный winws ещё работает. Останови его и повтори удаление.");
            }
        // Keep a shared WinDivert driver if another winws is still using it.
        Process[] remaining = Process.GetProcessesByName("winws");
        bool otherWinws = remaining.Length > 0;
        foreach (Process process in remaining) process.Dispose();
        if (otherWinws) throw new InvalidOperationException("Работает другая копия winws. Останови её перед удалением встроенного драйвера.");
        RemoveOwnedService("WinDivert", engine, true);
        RemoveOwnedService("WinDivert14", engine, true);
    }
    private static void RemoveOwnedService(string name, string engine, bool driver)
    {
        string image;
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
        { if (key == null) return; image = Environment.ExpandEnvironmentVariables((string)key.GetValue("ImagePath", "")); }
        if (image.StartsWith(@"\??\")) image = image.Substring(4);
        string executable = driver ? image.Trim('"') : Regex.Match(image, "^(?:\"([^\"]+)\"|(\\S+))").Groups[1].Value;
        if (!driver && executable.Length == 0) executable = Regex.Match(image, @"^\S+").Value;
        if (!Path.GetFullPath(executable).StartsWith(engine, StringComparison.OrdinalIgnoreCase)) return;
        using (ServiceController service = new ServiceController(name))
        {
            if (service.Status != ServiceControllerStatus.Stopped)
            { service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); }
        }
        using (Process remove = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), "delete " + name) { UseShellExecute = false, CreateNoWindow = true }))
            if (!remove.WaitForExit(10000) || remove.ExitCode != 0) throw new IOException("Не удалось удалить службу " + name + ". Удаление файлов остановлено.");
    }
}
