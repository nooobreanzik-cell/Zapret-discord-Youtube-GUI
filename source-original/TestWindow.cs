using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal sealed class TestWindow : SessionForm
{
    private readonly string folder;
    private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    private readonly CheckedListBox choices = new CheckedListBox { CheckOnClick = true, Dock = DockStyle.Fill };
    private readonly TextBox output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label state = Theme.Caption("Выбери альты и начни проверку", 13, true);
    private readonly Button run = new SoftButton { Text = "Начать проверку", AutoSize = true };
    private readonly Button cancel = new SoftButton { Text = "Остановить проверку", AutoSize = true, Enabled = false };
    private bool running, cancelled;
    private string cancelPath;
    internal TestWindow(string root, string selected, bool advanced)
    {
        folder = root; Text = "Проверить альты — Zapret GUI";
        Font = new Font("Segoe UI", 10F); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(840, 760); MinimumSize = new Size(720, 650); StartPosition = FormStartPosition.CenterParent;
        TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 42, 56, 40, 160, 52, 62 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Theme.Caption("Проверить альты", 22, true), 0, 0);
        layout.Controls.Add(Theme.Caption("Проверка временно остановит Zapret и переберёт выбранные настройки. После завершения прежний запуск будет восстановлен. Это может занять несколько минут.", 10, false), 0, 1);
        mode.Items.Add("Обычная проверка · HTTP и ping"); if (advanced) mode.Items.Add("Расширенная проверка · DPI"); mode.SelectedIndex = 0;
        layout.Controls.Add(mode, 0, 2);
        foreach (string file in Directory.GetFiles(root, "general*.bat").OrderBy(x => x)) choices.Items.Add(Path.GetFileName(file), true);
        layout.Controls.Add(choices, 0, 3);
        FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(run); actions.Controls.Add(cancel); layout.Controls.Add(actions, 0, 4);
        state.MaximumSize = new Size(750, 0); layout.Controls.Add(state, 0, 5); layout.Controls.Add(output, 0, 6); Controls.Add(layout);
        Theme.Apply(this); Theme.Primary(run);
        run.Click += delegate { Start(); };
        cancel.Click += delegate
        {
            if (!running) return;
            try { File.WriteAllText(cancelPath, "cancel"); cancelled = true; cancel.Enabled = false; state.Text = "Остановка после текущего альта. Восстанавливаем настройки…"; }
            catch (Exception error) { MessageBox.Show(this, error.Message); }
        };
        FormClosing += delegate(object s, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && running) { e.Cancel = true; state.Text = "Сначала нажми «Остановить проверку» и дождись восстановления настроек."; }
        };
    }
    private void Append(string line)
    {
        if (IsDisposed || Disposing) return;
        if (output.TextLength > 70000) output.Clear(); output.AppendText(line + "\r\n");
        Match progress = Regex.Match(line, @"\[(\d+)/(\d+)\]\s*(general.+)");
        if (progress.Success && !cancelled) state.Text = "Проверка " + progress.Groups[1].Value + " из " + progress.Groups[2].Value + ": " + progress.Groups[3].Value;
    }
    private void Start()
    {
        if (running || choices.CheckedItems.Count == 0) { state.Text = "Отметь хотя бы один альт."; return; }
        string files = string.Join("|", choices.CheckedItems.Cast<string>().ToArray());
        string kind = mode.SelectedIndex == 0 ? "standard" : "dpi";
        cancelPath = Path.Combine(folder, "utils", "gui-test-cancel.flag");
        try { if (File.Exists(cancelPath)) File.Delete(cancelPath); }
        catch (Exception error) { MessageBox.Show(this, error.Message); return; }
        running = true; cancelled = false; run.Enabled = mode.Enabled = choices.Enabled = false; cancel.Enabled = true; output.Clear(); state.Text = "Подготовка проверки…";
        DateTime started = DateTime.Now;
        BackgroundWorker worker = new BackgroundWorker();
        worker.DoWork += delegate(object s, DoWorkEventArgs e)
        {
            ZapretService.EnsureNoForeignService(folder);
            string script = TestAdapter.Create(folder);
            bool restore = ZapretService.Read(folder).Running;
            try
            {
                if (restore) ZapretService.Stop(folder);
                Process[] others = Process.GetProcessesByName("winws"); bool conflict = others.Length != 0; foreach (Process p in others) p.Dispose();
                if (conflict) throw new InvalidOperationException("Останови другую копию Zapret перед проверкой.");
                ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                info.EnvironmentVariables["GUI_TEST_TYPE"] = kind; info.EnvironmentVariables["GUI_TEST_CONFIGS"] = files; info.EnvironmentVariables["GUI_TEST_CANCEL"] = cancelPath;
                using (Process process = new Process { StartInfo = info })
                {
                    DataReceivedEventHandler receive = delegate(object sender, DataReceivedEventArgs data)
                    { if (data.Data != null && !IsDisposed && IsHandleCreated) try { BeginInvoke(new Action<string>(Append), data.Data); } catch (InvalidOperationException) { } };
                    process.OutputDataReceived += receive; process.ErrorDataReceived += receive;
                    process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
                    if (process.ExitCode != 0) throw new IOException("Тест завершился с ошибкой. Подробности — в журнале ниже.");
                }
            }
            finally
            {
                if (restore) using (ServiceController service = new ServiceController("zapret")) { service.Refresh(); if (service.Status == ServiceControllerStatus.Stopped) service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20)); }
                try { File.Delete(Path.Combine(folder, "gui-test-active.bat")); File.Delete(cancelPath); } catch { }
            }
            string reports = Path.Combine(folder, "utils", "test results");
            string report = Directory.Exists(reports) ? Directory.GetFiles(reports, "test_results_*.txt").Where(p => File.GetLastWriteTime(p) >= started.AddSeconds(-2)).OrderByDescending(File.GetLastWriteTime).FirstOrDefault() : null;
            if (report == null) throw new IOException("Новый отчёт не создан. Посмотри журнал проверки.");
            string text = File.ReadAllText(report); Match best = Regex.Match(text, @"(?m)^Best strategy:\s*(.+?)\s*$");
            Match score = best.Success ? Regex.Match(text, @"(?m)^" + Regex.Escape(best.Groups[1].Value.Trim()) + @"\s*:\s*(?:HTTP )?OK:\s*(\d+)") : Match.Empty;
            e.Result = score.Success && int.Parse(score.Groups[1].Value) > 0 ? "Лучший альт: " + best.Groups[1].Value.Trim() : "Успешных проверок нет. Подходящий альт не найден.";
        };
        worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
        {
            running = false;
            if (!IsDisposed) { run.Enabled = mode.Enabled = choices.Enabled = true; cancel.Enabled = false; state.Text = e.Error != null ? e.Error.Message : (cancelled ? "Проверка остановлена. Частичный результат: " : "") + (string)e.Result; state.ForeColor = e.Error == null ? Theme.Accent : Theme.Error; }
            worker.Dispose();
        };
        worker.RunWorkerAsync();
    }
}
