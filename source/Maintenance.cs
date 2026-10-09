using System;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
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
        RemoveNative(name);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStatus { internal uint Type, State, Accepted, Win32Exit, ServiceExit, Checkpoint, WaitHint; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string machine, string database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatus(IntPtr service, out NativeStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool ControlService(IntPtr service, uint command, out NativeStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DeleteService(IntPtr service);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
    private static Exception Failure(string name, string operation, int code)
    { return new IOException("Не удалось " + operation + " службу " + name + ". Код Windows: " + code + ". " + new Win32Exception(code).Message +
        (code == 5 ? " Запусти удаление от имени администратора." : "") + " Удаление файлов остановлено."); }
    private static void RemoveNative(string name)
    {
        IntPtr manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw Failure(name, "открыть диспетчер служб для", Marshal.GetLastWin32Error());
        try
        {
            IntPtr service = OpenService(manager, name, 0x00010024); // DELETE | STOP | QUERY_STATUS, including kernel drivers.
            if (service == IntPtr.Zero)
            {
                int code = Marshal.GetLastWin32Error();
                if (code == 1060) return; // Driver can disappear automatically after the engine closes it.
                if (code == 1072) throw new IOException("Служба " + name + " уже помечена на удаление. Закрой окно «Службы» и повтори удаление; если не помогло, перезагрузи Windows.");
                throw Failure(name, "открыть", code);
            }
            try
            {
                NativeStatus status;
                if (!QueryServiceStatus(service, out status)) throw Failure(name, "проверить", Marshal.GetLastWin32Error());
                if (status.State != 1 && status.State != 3 && !ControlService(service, 1, out status))
                {
                    int code = Marshal.GetLastWin32Error();
                    if (code != 1062) throw Failure(name, "остановить", code);
                }
                Stopwatch watch = Stopwatch.StartNew();
                while (true)
                {
                    if (!QueryServiceStatus(service, out status)) throw Failure(name, "проверить остановку", Marshal.GetLastWin32Error());
                    if (status.State == 1) break;
                    if (watch.Elapsed.TotalSeconds >= 20) throw new IOException("Служба " + name + " не остановилась за 20 секунд. Файлы не удалены; повтори после перезагрузки Windows.");
                    Thread.Sleep(100);
                }
                if (!DeleteService(service))
                {
                    int code = Marshal.GetLastWin32Error();
                    if (code != 1060 && code != 1072) throw Failure(name, "удалить", code);
                }
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
}
