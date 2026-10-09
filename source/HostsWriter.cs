using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;

internal static class HostsWriter
{
    private static IOException Denied(string path, string stage, UnauthorizedAccessException error)
    {
        bool admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        return Report(path, new IOException("Windows запретила операцию с hosts.\r\nЭтап: " + stage +
            "\r\nФайл: " + path + "\r\nПрава администратора: " + (admin ? "есть" : "нет") +
            "\r\nКод: 0x" + error.HResult.ToString("X8") +
            "\r\nЭто может быть ограничение прав файла или блокировка защитой Windows/антивирусом. " +
            "Подробности сохранены в журнале hosts.", error));
    }
    private static bool Sharing(IOException error) { int code = error.HResult & 0xffff; return code == 32 || code == 33; }
    private static IOException Busy(string path, Exception error)
    {
        string stage = error.Data["HostsStage"] as string ?? "чтение hosts";
        return Report(path, new IOException("Hosts не обновлён: Windows вернула конфликт доступа к файлу.\r\nЭтап: " + stage +
            "\r\nФайл: " + path + "\r\nКод Windows: " + (error.HResult & 0xffff) + " (0x" + error.HResult.ToString("X8") + ")" +
            "\r\nОтчёт о процессах добавлен в журнал hosts. Нажми «Скопировать журнал hosts». По одному коду нельзя определить виновника.", error));
    }
    private static IOException Report(string path, IOException error)
    {
        try { error.Data["HostsReport"] = HostsLockReport.Read(path); }
        catch (Exception diagnostic) { error.Data["HostsReport"] = "Диагностика недоступна: " + diagnostic.Message; }
        return error;
    }
    internal static string ReadHash(string path)
    {
        for (int attempt = 0; ; attempt++)
            try { return Files.Hash(path); }
            catch (UnauthorizedAccessException error) { throw Denied(path, "чтение исходного файла", error); }
            catch (IOException error) { if (!Sharing(error)) throw; if (attempt == 3) throw Busy(path, error); Thread.Sleep(200); }
    }
    internal static string Replace(string path, byte[] bytes, string expected)
    {
        FileAttributes original;
        try { original = File.Exists(path) ? File.GetAttributes(path) : FileAttributes.Normal; }
        catch (UnauthorizedAccessException error) { throw Denied(path, "чтение атрибутов файла", error); }
        bool readOnly = (original & FileAttributes.ReadOnly) != 0;
        bool cleared = false;
        string stage = "сохранение файла";
        try
        {
            if (readOnly) { stage = "снятие атрибута «Только чтение»"; File.SetAttributes(path, original & ~FileAttributes.ReadOnly); cleared = true; }
            stage = "сохранение файла";
            return ReplaceCore(path, bytes, expected);
        }
        catch (UnauthorizedAccessException error)
        {
            throw Denied(path, stage, error);
        }
        finally
        {
            if (cleared && File.Exists(path))
                try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly); }
                catch (UnauthorizedAccessException error) { throw Denied(path, "возврат атрибута «Только чтение» после операции; содержимое уже могло быть сохранено", error); }
        }
    }
    private static string ReplaceCore(string path, byte[] bytes, string expected)
    {
        // Prefer atomic replacement. Some readers allow writes but deny DELETE,
        // which prevents ReplaceFile even when ordinary file saving is allowed.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (ReadHash(path) != expected) throw new IOException("Hosts изменился во время операции. Повтори обновление; новое содержимое не перезаписано.");
            try { return Files.Replace(path, bytes); }
            catch (UnauthorizedAccessException) { break; }
            catch (IOException error) { int code = error.HResult & 0xffff; if (code == 5) break; if (!Sharing(error) && code != 1175) throw; Thread.Sleep(200); }
        }
        for (int attempt = 0; ; attempt++)
        {
            try { return SaveInPlace(path, bytes, expected); }
            catch (IOException error) { if (!Sharing(error)) throw; if (attempt == 3) throw Busy(path, error); Thread.Sleep(200); }
        }
    }
    private static string Hash(byte[] bytes)
    { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)); }
    private static string SaveInPlace(string path, byte[] bytes, string expected)
    {
        // No truncation on open. Prefer denying writers. A compatible existing
        // write handle may require shared opening; then lock the byte range
        // before reading, checking, backing up or changing any file content.
        FileStream opened;
        bool lockRange = false;
        try
        {
            try { opened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read); }
            catch (IOException error)
            {
                if (!Sharing(error)) throw;
                opened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                lockRange = true;
            }
        }
        catch (UnauthorizedAccessException error) { throw Denied(path, "открытие существующего hosts для записи (после отказа в замене)", error); }
        catch (IOException error) { error.Data["HostsStage"] = "открытие существующего hosts для записи"; throw; }
        using (FileStream file = opened)
        {
            if (bytes.Length > 8 * 1024 * 1024) throw new IOException("Новый hosts слишком велик для сохранения.");
            if (lockRange)
                try { file.Lock(0, 8 * 1024 * 1024 + 1); }
                catch (IOException error) { error.Data["HostsStage"] = "защита диапазона hosts от одновременной записи"; throw; }
            // Closing this handle releases the range lock on every exit path.
            if (file.Length > 8 * 1024 * 1024) throw new IOException("Hosts слишком велик для безопасного сохранения.");
            byte[] original = new byte[(int)file.Length]; int offset = 0;
            while (offset < original.Length) { int n = file.Read(original, offset, original.Length-offset); if (n == 0) throw new IOException("Не удалось полностью прочитать исходный hosts."); offset += n; }
            if (Hash(original) != expected) throw new IOException("Hosts изменился во время операции. Повтори обновление; файл сохранён без изменений.");
            string backupFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretSimpleGui", "hosts-backups");
            string backup = Path.Combine(backupFolder, "hosts.zapretgui." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0,6) + ".bak");
            try
            {
                Directory.CreateDirectory(backupFolder);
                using (FileStream copy = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                { copy.Write(original, 0, original.Length); copy.Flush(true); }
            }
            catch (UnauthorizedAccessException error) { throw Denied(backup, "создание резервной копии; hosts ещё не изменён", error); }
            try
            {
                file.Position = 0; file.Write(bytes, 0, bytes.Length); file.SetLength(bytes.Length); file.Flush(true);
                file.Position = 0;
                using (SHA256 sha = SHA256.Create()) if (BitConverter.ToString(sha.ComputeHash(file)) != Hash(bytes)) throw new IOException("Не совпало содержимое после записи.");
            }
            catch (Exception writeError)
            {
                try { file.Position = 0; file.Write(original, 0, original.Length); file.SetLength(original.Length); file.Flush(true); }
                catch (Exception restoreError) { throw new IOException("Запись hosts прервана; автоматический откат тоже не удался. Восстанови резервную копию: " + backup + ". Ошибка: " + restoreError.Message, writeError); }
                throw new IOException("Не удалось сохранить hosts. Исходное содержимое восстановлено. Копия: " + backup, writeError);
            }
            return backup;
        }
    }
}

// Query Restart Manager only. Never call RmShutdown or close another process's handles.
internal static class HostsLockReport
{
    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess { internal uint Pid; internal System.Runtime.InteropServices.ComTypes.FILETIME Started; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        internal UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Service;
        internal uint Kind, Status, Session;
        [MarshalAs(UnmanagedType.Bool)] internal bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RmStartSession(out uint handle, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RmRegisterResources(uint handle, uint count, string[] files, uint apps, IntPtr processes, uint services, IntPtr names);
    [DllImport("rstrtmgr.dll", ExactSpelling = true)]
    private static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] ProcessInfo[] info, ref uint reasons);
    [DllImport("rstrtmgr.dll", ExactSpelling = true)] private static extern int RmEndSession(uint handle);
    internal static string Read(string path)
    {
        StringBuilder result = new StringBuilder("ДИАГНОСТИКА HOSTS — " + DateTime.Now.ToString("g") + "\r\n" + path + "\r\n");
        try { result.AppendLine("Атрибуты: " + File.GetAttributes(path)); }
        catch (Exception e) { result.AppendLine("Атрибуты недоступны: " + e.Message); }
        uint handle; int code = RmStartSession(out handle, 0, new StringBuilder(33));
        if (code != 0) return result + "Windows не запустила диагностику: код " + code;
        try
        {
            code = RmRegisterResources(handle, 1, new[] { path }, 0, IntPtr.Zero, 0, IntPtr.Zero);
            if (code != 0) return result + "Windows не зарегистрировала файл для проверки: код " + code;
            uint needed = 0, count = 0, reasons = 0;
            code = RmGetList(handle, out needed, ref count, null, ref reasons);
            for (int attempt = 0; code == 234 && attempt < 3; attempt++)
            {
                if (needed > 4096) return result + "Windows вернула слишком большой список процессов.";
                ProcessInfo[] info = new ProcessInfo[(int)needed]; count = needed;
                code = RmGetList(handle, out needed, ref count, info, ref reasons);
                if (code == 0)
                    for (int i = 0; i < (int)count; i++)
                        result.AppendLine("PID " + info[i].Process.Pid + " · " + info[i].Name + (string.IsNullOrEmpty(info[i].Service) ? "" : " · служба " + info[i].Service));
            }
            if (code != 0) result.AppendLine("Windows не завершила запрос: код " + code);
            else if (count == 0) result.AppendLine("Windows не назвала процессы, использующие hosts. Это не исключает блокировку драйвером защиты или уже завершившимся процессом.");
            else result.AppendLine("Выше перечислены процессы, использующие файл. Само присутствие в списке ещё не доказывает, что именно этот процесс запрещает запись.");
            result.AppendLine("Процессы не закрывались, файл и настройки защиты не менялись. Скопируй этот журнал и пришли его для разбора.");
            return result.ToString();
        }
        finally { RmEndSession(handle); }
    }
}

internal static class HostsManual
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
    private static string Downloads()
    {
        Guid id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); IntPtr pointer;
        int code = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out pointer);
        if (code != 0) Marshal.ThrowExceptionForHR(code);
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }
    internal static string Prepare(byte[] bytes)
    {
        Files.ValidateHosts(bytes);
        string directory = Path.Combine(Downloads(), "Zapret-hosts-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "hosts");
        using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        string note = "";
        try { File.Copy(HostsGuard.SystemHosts, Path.Combine(directory, "hosts-before.bak"), false); }
        catch (Exception error) { note = "\r\nАвтоматическая резервная копия недоступна: " + error.Message + "\r\nСохрани прежний hosts вручную перед заменой."; }
        File.WriteAllText(Path.Combine(directory, "Инструкция.txt"), Instructions(path) + note, new UTF8Encoding(true));
        return path;
    }
    internal static string Instructions(string path)
    {
        return "РУЧНАЯ ЗАМЕНА HOSTS\r\nФайл подготовлен: " + path +
            "\r\nСистемный hosts пока не заменён.\r\n\r\n" +
            "1. Сохрани копию старого hosts перед заменой. Если удалось прочитать файл, рядом с новым сохранён hosts-before.bak.\r\n" +
            "2. Скопируй подготовленный файл hosts без расширения в " + Path.GetDirectoryName(HostsGuard.SystemHosts) + ".\r\n" +
            "3. Подтверди замену и запрос администратора в Проводнике.\r\n\r\n" +
            "Если Windows снова запрещает замену, проверь журнал антивируса. Если он подтверждает блокировку, разреши эту операцию в его настройках только если доверяешь приложению и списку. Ручное копирование также может блокироваться защитой.\r\n";
    }
    internal static string OpenFolders(string path)
    {
        string result = "";
        foreach (string file in new[] { path, HostsGuard.SystemHosts })
            try
            {
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "/select,\"" + file + "\"") { UseShellExecute = true })) { }
            }
            catch (Exception error) { result += "\r\nНе удалось открыть папку: " + Path.GetDirectoryName(file) + ". " + error.Message; }
        return result;
    }
}
