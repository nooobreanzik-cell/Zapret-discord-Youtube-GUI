using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

internal static class HostsWriter
{
    private static bool Sharing(IOException error) { int code = error.HResult & 0xffff; return code == 32 || code == 33; }
    private static IOException Busy(string path, Exception error)
    { return new IOException("Windows не разрешила запись в " + path + ". Файл удерживается другим процессом. Закрой редакторы hosts и повтори попытку. Если ошибка остаётся, проверь журнал защиты антивируса. Программа не отключает защиту и не снимает блокировки принудительно.", error); }
    internal static string ReadHash(string path)
    {
        for (int attempt = 0; ; attempt++)
            try { return Files.Hash(path); }
            catch (IOException error) { if (!Sharing(error)) throw; if (attempt == 3) throw Busy(path, error); Thread.Sleep(200); }
    }
    internal static string Replace(string path, byte[] bytes, string expected)
    {
        // Prefer atomic replacement. Some readers allow writes but deny DELETE,
        // which prevents ReplaceFile even when ordinary file saving is allowed.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (ReadHash(path) != expected) throw new IOException("Hosts изменился во время операции. Повтори обновление; новое содержимое не перезаписано.");
            try { return Files.Replace(path, bytes); }
            catch (IOException error) { if (!Sharing(error) && (error.HResult & 0xffff) != 1175) throw; Thread.Sleep(200); }
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
        // No truncation on open. Deny concurrent writers while preserving readers.
        using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            if (file.Length > 8 * 1024 * 1024) throw new IOException("Hosts слишком велик для безопасного сохранения.");
            byte[] original = new byte[(int)file.Length]; int offset = 0;
            while (offset < original.Length) { int n = file.Read(original, offset, original.Length-offset); if (n == 0) throw new IOException("Не удалось полностью прочитать исходный hosts."); offset += n; }
            if (Hash(original) != expected) throw new IOException("Hosts изменился во время операции. Повтори обновление; файл сохранён без изменений.");
            string backup = path + ".zapretgui." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0,6) + ".bak";
            using (FileStream copy = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { copy.Write(original, 0, original.Length); copy.Flush(true); }
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
