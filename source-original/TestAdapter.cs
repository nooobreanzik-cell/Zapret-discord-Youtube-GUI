using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

internal static class TestAdapter
{
    private static string Function(string source, string name, string body)
    {
        Regex function = new Regex(@"(?ms)^function " + Regex.Escape(name) + @" \{.*?^\}");
        if (function.Matches(source).Count != 1) throw new InvalidDataException("Изменился формат тестов Flowseal: " + name);
        return function.Replace(source, delegate(Match m) { return "function " + name + " {\r\n" + body + "\r\n}"; }, 1);
    }
    internal static string Create(string root)
    {
        string source = File.ReadAllText(Path.Combine(root, "utils", "test zapret.ps1"));
        source = Function(source, "Wait-AnyKey", "    param([string]$message)\r\n    return");
        source = Function(source, "Read-TestType", "    return $env:GUI_TEST_TYPE");
        source = Function(source, "Read-ModeSelection", "    return 'select'");
        source = Function(source, "Read-ConfigSelection", "    param([array]$allFiles)\r\n    $names = $env:GUI_TEST_CONFIGS -split '\\|'\r\n    return @($allFiles | Where-Object { $names -contains $_.Name })");
        source = source.Replace("-Filter \"*.bat\"", "-Filter \"general*.bat\"");
        string loop = "foreach ($file in $batFiles) {";
        if (!source.Contains(loop) || !source.Contains("$($file.FullName)")) throw new InvalidDataException("Изменился цикл тестирования Flowseal.");
        source = source.Replace(loop, loop + "\r\n    if (Test-Path -LiteralPath $env:GUI_TEST_CANCEL) { break }");
        string launch = "    $proc = Start-Process -FilePath \"cmd.exe\"";
        if (!source.Contains(launch)) throw new InvalidDataException("Не найден запуск тестируемого альта.");
        source = source.Replace(launch,
            "    $guiBat = Join-Path $targetDir 'gui-test-active.bat'\r\n" +
            "    $guiText = [IO.File]::ReadAllText($file.FullName)\r\n" +
            "    $guiText = $guiText -replace '(?im)^start\\s+\"[^\"]*\"\\s+/min\\s+', 'start \"\" /b '\r\n" +
            "    [IO.File]::WriteAllText($guiBat, $guiText, (New-Object Text.UTF8Encoding($false)))\r\n" + launch);
        source = source.Replace("$($file.FullName)", "$guiBat").Replace("-WindowStyle Minimized", "-WindowStyle Hidden");
        if (source.Contains("Read-Host") || source.Contains("Console]::ReadKey")) throw new InvalidDataException("Остался интерактивный вопрос в скрипте тестов.");
        string path = Path.Combine(root, "utils", "gui-tests.ps1");
        File.WriteAllText(path, "[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)\r\n$ProgressPreference = 'SilentlyContinue'\r\n" + source, new UTF8Encoding(true));
        return path;
    }
}
