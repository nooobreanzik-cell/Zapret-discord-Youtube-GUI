using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

internal static class Files
{
    internal static string Replace(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string suffix = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        string temp = path + ".gui-" + suffix + ".tmp";
        string backup = path + ".zapretgui." + suffix + ".bak";
        try
        {
            File.WriteAllBytes(temp, content);
            if (File.Exists(path)) File.Replace(temp, path, backup, false);
            else { File.Move(temp, path); backup = ""; }
            return backup;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static string Hash(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (SHA256 hash = SHA256.Create())
        using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) return BitConverter.ToString(hash.ComputeHash(file));
    }
    internal static string Text(byte[] bytes)
    {
        string text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
        if (text.IndexOf('\0') >= 0 || Regex.IsMatch(text, @"<\s*(?:html|!doctype)", RegexOptions.IgnoreCase))
            throw new InvalidDataException("Вместо списка получена веб-страница или повреждённый файл.");
        return text;
    }
    internal static int ValidateHosts(byte[] bytes)
    {
        int count = 0;
        foreach (string raw in Text(bytes).Split('\n'))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            string[] parts = Regex.Split(line, @"\s+");
            IPAddress ip;
            if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out ip)) throw new InvalidDataException("Некорректная строка в hosts: " + line.Substring(0, Math.Min(100, line.Length)));
            foreach (string domain in parts.Skip(1))
                if (domain.Length > 253 || !Regex.IsMatch(domain, @"^[a-zA-Z0-9_][a-zA-Z0-9_.-]*$")) throw new InvalidDataException("Некорректное имя в hosts: " + domain);
            count += parts.Length - 1;
        }
        if (count == 0) throw new InvalidDataException("Пустой hosts не будет применён.");
        return count;
    }
    internal static int ValidateIpset(byte[] bytes)
    {
        int count = 0;
        foreach (string raw in Text(bytes).Split('\n'))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            string[] parts = line.Split('/'); IPAddress ip; int prefix;
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out ip)) throw new InvalidDataException("Некорректный IP-набор.");
            int max = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > max)) throw new InvalidDataException("Некорректная маска IP-набора.");
            count++;
        }
        if (count == 0) throw new InvalidDataException("Получен пустой IP-набор.");
        return count;
    }
    internal static bool ValidPorts(string value)
    {
        if (!Regex.IsMatch(value, @"^[1-9]\d*(?:-[1-9]\d*)?(?:,[1-9]\d*(?:-[1-9]\d*)?)*$")) return false;
        foreach (string range in value.Split(','))
        {
            string[] parts = range.Split('-'); int begin, end;
            if (!int.TryParse(parts[0], out begin) || begin < 1 || begin > 65535) return false;
            if (parts.Length == 2 && (!int.TryParse(parts[1], out end) || end < begin || end > 65535)) return false;
        }
        return true;
    }
}

internal static class NetworkFiles
{
    internal static readonly string HostsUrl = "https://raw.githubusercontent.com/V3nilla/IPSets-For-Bypass-in-Russia/main/" + Uri.EscapeDataString("Разблокировка множества сервисов(пример - ChatGPT)") + "/hosts";
    internal const string VanillaIpset = "https://raw.githubusercontent.com/V3nilla/IPSets-For-Bypass-in-Russia/main/ipset-all.txt";
    internal const string FlowsealIpset = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/ipset-service.txt";
    internal const string VersionUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";
    internal static byte[] Download(string url, int limit)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Timeout = 15000; request.ReadWriteTimeout = 15000;
        request.UserAgent = "Zapret-GUI/0.3";
        request.AllowAutoRedirect = false;
        request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        {
            if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > limit) throw new IOException("Сервер вернул неожиданный ответ. Исходные файлы сохранены.");
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream data = new MemoryStream())
            {
                byte[] buffer = new byte[16384]; int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                { if (data.Length + n > limit) throw new IOException("Загруженный файл слишком велик."); data.Write(buffer, 0, n); }
                return data.ToArray();
            }
        }
    }
    internal static string LatestVersion()
    {
        string value = Files.Text(Download(VersionUrl, 1024)).Trim();
        if (!Regex.IsMatch(value, @"^\d+\.\d+(?:\.\d+){0,2}(?:[a-zA-Z0-9.-]*)$")) throw new InvalidDataException("Не удалось прочитать версию Zapret.");
        return value;
    }
}

internal static class ServiceBridge
{
    internal static readonly string[] Actions = { "service_install", "service_remove", "service_status", "service_diagnostics" };
    internal static string CreateAdapter(string folder)
    {
        string source = File.ReadAllText(Path.Combine(folder, "service.bat"));
        foreach (string label in Actions) if (!source.Contains(":" + label)) throw new InvalidDataException("Эта версия service.bat несовместима с GUI: нет " + label);
        // Use upstream parsing of strategy arguments, with the selection supplied
        // by the GUI. Only the menu/pauses are adapted; engine arguments stay intact.
        int install = source.IndexOf(":service_install\n", StringComparison.Ordinal);
        if (install < 0) install = source.IndexOf(":service_install\r\n", StringComparison.Ordinal);
        if (install < 0) throw new InvalidDataException("Не найден раздел установки службы.");
        int begin = source.IndexOf("echo Pick one of the options:", install, StringComparison.Ordinal);
        if (begin < 0) throw new InvalidDataException("Изменился раздел выбора стратегии.");
        int end = source.IndexOf(":: Args that should be followed by value", begin, StringComparison.Ordinal);
        if (install < 0 || begin < 0 || end < 0) throw new InvalidDataException("Изменился формат service.bat. Установка службы остановлена.");
        source = source.Substring(0, begin) + "set \"selectedFile=%GUI_STRATEGY%\"\r\nif not exist \"!selectedFile!\" exit /b 2\r\n\r\n" + source.Substring(end);
        source = source.Replace("!file%choice%!", "!selectedFile!");
        source = source.Replace("start= auto", "start= %GUI_START_TYPE%");
        source = Regex.Replace(source, @"(?im)^\s*goto menu\s*$", "goto gui_done");
        source = Regex.Replace(source, @"(?im)^pause\s*$", "if not \"%GUI_SILENT%\"==\"1\" pause");
        source = Regex.Replace(source, @"(?im)^cls\s*$", "if not \"%GUI_SILENT%\"==\"1\" cls");
        Match version = Regex.Match(source, @"set ""LOCAL_VERSION=([^""]+)""");
        string header = "@echo off\r\ncd /d \"%~dp0\"\r\nsetlocal EnableDelayedExpansion\r\n";
        if (version.Success) header += "set \"LOCAL_VERSION=" + version.Groups[1].Value + "\"\r\n";
        header += "call :load_user_lists\r\ncall :game_switch_status\r\ncall :ipset_switch_status\r\ncall :check_updates_switch_status\r\n";
        foreach (string action in Actions) header += "if \"%~1\"==\"" + action + "\" goto " + action + "\r\n";
        header += "exit /b 2\r\n";
        string path = Path.Combine(folder, "gui-service.bat");
        File.WriteAllText(path, header + source + "\r\n:gui_done\r\nexit /b\r\n", new UTF8Encoding(false));
        return path;
    }
    internal static Process Launch(string folder, string action, string strategy, bool capture, bool automatic = true)
    {
        if (!Actions.Contains(action)) throw new ArgumentException("Неизвестное действие.");
        string adapter = CreateAdapter(folder);
        if (action == "service_install" && (Path.GetFileName(strategy) != strategy || !File.Exists(Path.Combine(folder, strategy)))) throw new ArgumentException("Выбери существующую стратегию.");
        ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        info.Arguments = "/d /s /v:off /c \"\"%GUI_ADAPTER%\" " + action + "\"";
        info.WorkingDirectory = folder; info.UseShellExecute = false;
        info.EnvironmentVariables["GUI_ADAPTER"] = adapter;
        info.EnvironmentVariables["GUI_START_TYPE"] = automatic ? "auto" : "demand";
        info.EnvironmentVariables["GUI_STRATEGY"] = strategy;
        info.EnvironmentVariables["GUI_SILENT"] = capture ? "1" : "0";
        info.EnvironmentVariables["NO_UPDATE_CHECK"] = "1";
        info.CreateNoWindow = capture;
        info.RedirectStandardOutput = capture; info.RedirectStandardError = capture;
        return Process.Start(info);
    }
    internal static string Run(string folder, string action, string strategy, bool automatic = true)
    {
        using (Process process = Launch(folder, action, strategy, true, automatic))
        {
            StringBuilder output = new StringBuilder(); object sync = new object();
            process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock(sync) output.AppendLine(e.Data); };
            process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock(sync) output.AppendLine(e.Data); };
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            if (!process.WaitForExit(60000)) { try { process.Kill(); } catch { } throw new TimeoutException("Штатный скрипт не завершился за минуту. Проверь состояние службы перед повтором."); }
            process.WaitForExit();
            lock(sync) return output.ToString();
        }
    }
}
