using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal static class Discovery
{
    internal sealed class Result
    {
        public List<string> Folders = new List<string>();
        public int Checked;
        public string Reason = "";
    }

    internal static bool IsBundle(string path)
    {
        try
        {
            return Directory.Exists(path) && File.Exists(Path.Combine(path, "service.bat")) &&
                File.Exists(Path.Combine(path, "bin", "winws.exe")) &&
                Directory.EnumerateFiles(path, "general*.bat", SearchOption.TopDirectoryOnly).Any();
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (System.Security.SecurityException) { return false; }
    }

    internal static bool Skip(string path)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        string[] excluded = { "Windows", "$Recycle.Bin", "System Volume Information", "WinSxS", "node_modules", ".git", "AppData", "ProgramData", "Recovery" };
        if (excluded.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool Named(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        return name.Contains("zapret") || name.Contains("запрет");
    }

    internal static Result Scan(BackgroundWorker worker)
    {
        Result result = new Result();
        LinkedList<string> queue = new LinkedList<string>();
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> roots = new List<string>();
        roots.Add(AppDomain.CurrentDomain.BaseDirectory);
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\ZapretSimpleGui"))
            if (key != null) roots.Add(key.GetValue("SearchHint", "") as string);
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        roots.Add(Downloads());
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) roots.Add(drive.RootDirectory.FullName); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || root.StartsWith(@"\\")) continue;
            try
            {
                if (new DriveInfo(Path.GetPathRoot(root)).DriveType == DriveType.Fixed && Directory.Exists(root)) queue.AddLast(root);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }

        Stopwatch elapsed = Stopwatch.StartNew();
        while (queue.Count > 0)
        {
            if (worker.CancellationPending) { result.Reason = "Поиск остановлен пользователем."; break; }
            if (elapsed.Elapsed.TotalSeconds > 60 || result.Checked >= 100000)
            { result.Reason = "Достигнут предел поиска; часть папок могла остаться непроверенной."; break; }
            string path = queue.First.Value;
            queue.RemoveFirst();
            try
            {
                path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!visited.Add(path)) continue;
                result.Checked++;
                if (result.Checked % 200 == 0) worker.ReportProgress(result.Checked);
                if (IsBundle(path))
                {
                    result.Folders.Add(path);
                    continue; // The internals of a found bundle do not need scanning.
                }
                // Push likely names to the front; every other directory still gets tested.
                foreach (string child in Directory.EnumerateDirectories(path))
                {
                    if (worker.CancellationPending) break;
                    try
                    {
                        if (Skip(child)) continue;
                        if (Named(child)) queue.AddFirst(child); else queue.AddLast(child);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }
        result.Folders.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static string Downloads()
    {
        IntPtr pointer;
        Guid id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out pointer) == 0)
        {
            try { return Marshal.PtrToStringUni(pointer); }
            finally { Marshal.FreeCoTaskMem(pointer); }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
