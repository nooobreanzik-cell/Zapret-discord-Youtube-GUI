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
    internal static void CheckFiles(string folder)
    {
        foreach (string name in new[] { "winws.exe", "WinDivert.dll", "WinDivert64.sys", "cygwin1.dll" })
            if (!File.Exists(Path.Combine(folder, "bin", name)))
                throw new IOException("Не найден или недоступен файл bin\\" + name + ". Запуск Zapret невозможен. Проверь папку установки и журнал/карантин антивируса. Автозапуск не изменён.");
    }
    private static string Sc(string arguments, bool requireSuccess)
    {
        System.Text.StringBuilder output = new System.Text.StringBuilder(); object sync = new object();
        ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.StandardOutputEncoding = info.StandardErrorEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        using (Process process = new Process { StartInfo = info })
        {
            DataReceivedEventHandler receive = delegate(object sender, DataReceivedEventArgs e)
            { if (e.Data != null) lock(sync) output.AppendLine(e.Data); };
            process.OutputDataReceived += receive; process.ErrorDataReceived += receive;
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            if (!process.WaitForExit(10000)) { try { process.Kill(); } catch { } throw new IOException("Windows не ответила на запрос к службе за 10 секунд."); }
            process.WaitForExit();
            lock(sync)
            {
                if (requireSuccess && process.ExitCode != 0) throw new IOException("Windows не изменила автозапуск. Код " + process.ExitCode + ":\r\n" + output);
                return output.ToString();
            }
        }
    }
    internal static string Diagnose(string folder)
    {
        System.Text.StringBuilder result = new System.Text.StringBuilder();
        result.AppendLine("ДИАГНОСТИКА АВТОЗАПУСКА — " + DateTime.Now.ToString("g"));
        result.AppendLine("Папка: " + folder);
        result.AppendLine("Автозапуск запускает службу Zapret в фоне; окно оболочки автоматически не открывается.");
        foreach (string name in new[] { "winws.exe", "WinDivert.dll", "WinDivert64.sys", "cygwin1.dll" })
            result.AppendLine(name + ": " + (File.Exists(Path.Combine(folder, "bin", name)) ? "файл найден" : "НЕТ ФАЙЛА / НЕТ ДОСТУПА"));
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret"))
        {
            if (key == null) result.AppendLine("Служба zapret не установлена.");
            else
            {
                int start = Convert.ToInt32(key.GetValue("Start", 3));
                result.AppendLine("Автозапуск: " + (start == 2 ? "включён" : start == 3 ? "выключен (ручной запуск)" : "Start=" + start));
                result.AppendLine("Команда службы: " + Convert.ToString(key.GetValue("ImagePath", "")));
            }
        }
        result.AppendLine(Sc("qc zapret 8192", false));
        result.AppendLine(Sc("queryex zapret", false));
        result.AppendLine(Sc("query WinDivert", false));
        result.AppendLine("Наличие файла не доказывает, что антивирус разрешает его загрузку. Если запуск не работает, приложи этот отчёт и событие из журнала антивируса.");
        return result.ToString();
    }
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
        if (enabled) CheckFiles(folder);
        Sc("config zapret start= " + (enabled ? "auto" : "demand"), true);
        if (Read(folder).Automatic != enabled) throw new IOException("Windows не подтвердила изменение автозагрузки.");
    }
}
