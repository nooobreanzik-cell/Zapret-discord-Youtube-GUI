using System;
using System.IO;

internal static class HostsGuard
{
    internal static string SystemHosts { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "etc", "hosts"); } }
    private static string Saved { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretSimpleGui", "hosts-last-applied.txt"); } }
    internal static void Remember(byte[] bytes) { Files.Replace(Saved, bytes); }
    internal static string Check()
    {
        if (!File.Exists(Saved)) return "";
        return Files.Hash(Saved) == Files.Hash(SystemHosts) ? "" : "Hosts изменён другой программой. Нажми здесь, чтобы проверить его и восстановить копию.";
    }
    internal static string Restore()
    {
        if (!File.Exists(Saved)) throw new IOException("Нет сохранённого hosts. Сначала примени hosts через оболочку.");
        byte[] bytes = File.ReadAllBytes(Saved);
        string backup = HostsWriter.Replace(SystemHosts, bytes, HostsWriter.ReadHash(SystemHosts));
        return "Последний применённый hosts восстановлен. Копия предыдущего: " + backup;
    }
}
