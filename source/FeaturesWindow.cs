using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;

internal sealed class FeaturesWindow : SessionForm
{
    private readonly string folder, strategy;
    private readonly Func<bool> stopOwn;
    private readonly SettingsPages tabs = new SettingsPages();
    private readonly bool advancedMode;
    private readonly Label notice = new Label { Dock = DockStyle.Bottom, Height = 76, Padding = new Padding(18, 12, 18, 8), Text = "Выбери нужное действие. Обновления применяются только по нажатию кнопки." };
    private readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 160 };
    private readonly ComboBox game = Choice("Выключен", "TCP и UDP", "Только TCP", "Только UDP");
    private readonly TextBox tcp = new TextBox { Text = "1024-65535", Width = 320 };
    private readonly TextBox udp = new TextBox { Text = "1024-65535", Width = 320 };
    private readonly ComboBox ipset = Choice("По списку (loaded)", "Отключён (none)", "Все адреса (any)");
    private readonly CheckBox autoUpdate = new SoftToggle { Text = "Проверять обновления при запуске стратегии", AutoSize = true };
    private readonly ComboBox ipSource = Choice("V3nilla", "Flowseal");
    private readonly ComboBox fakeType = Choice("Discord UDP", "Game Filter UDP");
    private readonly ComboBox fakeFile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    private bool busy;
    private Process tool;
    private readonly System.Windows.Forms.Timer toolTimer = new System.Windows.Forms.Timer { Interval = 500 };

    internal FeaturesWindow(string root, string selected, Func<bool> stop, bool advanced, string initialPage)
    {
        advancedMode = advanced;
        folder = root; strategy = selected; stopOwn = stop;
        Text = "Настройки — Zapret GUI";
        Font = new Font("Segoe UI", 10F); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(960, 740); MinimumSize = new Size(880, 650);
        StartPosition = FormStartPosition.CenterParent;
        Controls.Add(tabs); Controls.Add(log); Controls.Add(notice);
        log.Visible = advancedMode; notice.Visible = !advancedMode;
        FlowLayoutPanel services = Page("Служба", true);
        Label(services, "Служба Windows запускает Zapret в фоне без окна BAT. Удаление служб — техническое действие; для обычного выключения достаточно кнопки «Остановить».");
        Label(services, "Выбранный альт: " + strategy);
        Button(services, "Установить выбранный альт как службу (автозапуск)", delegate
        {
            if (!stopOwn()) return;
            Work(delegate { ZapretService.EnsureNoForeignService(folder); return ServiceBridge.Run(folder, "service_install", strategy) + "\r\n" + Status(); });
        });
        Button(services, "Запустить службу", delegate { ServiceAction(true); });
        Button(services, "Остановить службу", delegate { ServiceAction(false); });
        Button(services, "Удалить службы Zapret / WinDivert", delegate
        {
            if (MessageBox.Show(this, "Штатный скрипт остановит все winws.exe и удалит службы zapret, WinDivert и WinDivert14. Продолжить?", "Удаление служб", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (!stopOwn()) return;
            Work(delegate { return ServiceBridge.Run(folder, "service_remove", strategy) + "\r\n" + Status(); });
        });
        Button(services, "Проверить статус", delegate { Work(delegate { return ServiceBridge.Run(folder, "service_status", strategy); }); });
        Label(services, "Zapret запускается как служба и работает независимо от GUI. Кнопка «Остановить» выключает Zapret; крестик главного окна сворачивает GUI в трей.");

        FlowLayoutPanel filters = Page("Фильтры", true);
        Label(filters, "Game Filter — обработка трафика на выбранных игровых портах. TCP и UDP — два типа сетевого обмена. Если не уверен, оставь текущие значения."); filters.Controls.Add(game);
        Label(filters, "TCP: порты или диапазоны через запятую"); filters.Controls.Add(tcp);
        Label(filters, "UDP: порты или диапазоны через запятую"); filters.Controls.Add(udp);
        Button(filters, "Сохранить Game Filter", SaveGame);
        Label(filters, "IPSet Filter определяет адреса для обработки: loaded — только из списка, none — отключён, any — все адреса. После изменения перезапусти Zapret."); filters.Controls.Add(ipset);
        Button(filters, "Применить режим IPSet", SaveIpsetMode);
        filters.Controls.Add(autoUpdate);
        Button(filters, "Сохранить проверку обновлений", delegate
        {
            try
            {
                string path = Path.Combine(folder, "utils", "check_updates.enabled");
                if (autoUpdate.Checked) Files.Replace(path, Encoding.ASCII.GetBytes("ENABLED\r\n"));
                else if (File.Exists(path)) File.Delete(path);
                Append("Настройка проверки обновлений сохранена.");
            }
            catch (Exception e) { ShowError(e); }
        });

        FlowLayoutPanel fakes = Page("Fake-файлы", true);
        Label(fakes, "Fake-файлы — образцы пакетов, которые использует стратегия обхода DPI. Замена может изменить результат работы. Предыдущий файл сохраняется в резервной копии.");
        Label(fakes, "Какой активный fake заменить"); fakes.Controls.Add(fakeType);
        Label(fakes, "Файл из папки bin"); fakes.Controls.Add(fakeFile);
        foreach (string file in Directory.GetFiles(Path.Combine(folder, "bin"), "*.bin").OrderBy(Path.GetFileName))
            if (!Path.GetFileName(file).StartsWith("ACTIVE_", StringComparison.OrdinalIgnoreCase)) fakeFile.Items.Add(Path.GetFileName(file));
        if (fakeFile.Items.Count > 0) fakeFile.SelectedIndex = 0;
        Button(fakes, "Заменить активный fake", delegate
        {
            if (fakeFile.SelectedItem == null) return;
            try
            {
                string target = Path.Combine(folder, "bin", fakeType.SelectedIndex == 0 ? "ACTIVE_DISCORD_UDP.bin" : "ACTIVE_GAME_UDP.bin");
                string backup = Files.Replace(target, File.ReadAllBytes(Path.Combine(folder, "bin", (string)fakeFile.SelectedItem)));
                Append("Fake заменён. Для применения перезапусти Zapret. Резервная копия: " + backup);
            }
            catch (Exception e) { ShowError(e); }
        });
        Button(fakes, "Показать активные fake-файлы", delegate
        {
            foreach (string name in new[] { "ACTIVE_DISCORD_UDP.bin", "ACTIVE_GAME_UDP.bin" })
            {
                string hash = Files.Hash(Path.Combine(folder, "bin", name));
                string match = fakeFile.Items.Cast<string>().FirstOrDefault(f => Files.Hash(Path.Combine(folder, "bin", f)) == hash);
                Append(name + ": " + (match ?? "нет совпадения среди доступных файлов"));
            }
        });

        FlowLayoutPanel updates = Page("Списки и обновления", false);
        Label(updates, "Hosts — таблица соответствия сайтов и IP-адресов в Windows. Кнопка ниже заменяет её списком V3nilla, сохраняя предыдущую версию для восстановления.");
        Button(updates, "Обновить hosts · V3nilla", delegate { UpdateHosts(null); });
        Button(updates, "Скачать hosts V3nilla для ручной замены", delegate
        {
            Work(delegate
            {
                byte[] bytes = NetworkFiles.Download(NetworkFiles.HostsUrl, 8 * 1024 * 1024);
                Files.ValidateHosts(bytes);
                string path = HostsManual.Prepare(new UTF8Encoding(false).GetBytes(Files.Text(bytes)));
                return HostsManual.Instructions(path) + HostsManual.OpenFolders(path);
            });
        });
        Button(updates, "Кто использует hosts?", delegate
        {
            log.Visible = true; notice.Visible = false;
            Work(delegate { return HostsLockReport.Read(HostsPath); });
        });
        Button(updates, "Скопировать журнал hosts", delegate { if (log.TextLength > 0) Clipboard.SetText(log.Text); });
        Button(updates, "Заменить hosts из локального файла…", delegate
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "Выбери скачанный hosts", Filter = "Все файлы|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK) UpdateHosts(dialog.FileName);
        });
        Button(updates, "Восстановить hosts из резервной копии…", RestoreHosts);
        Button(updates, "Проверить, не изменился ли hosts", delegate { Work(delegate { string changed = HostsGuard.Check(); return changed.Length == 0 ? "Изменение не обнаружено (контроль начинается после применения hosts через GUI)." : changed; }); });
        Button(updates, "Вернуть последний применённый hosts", delegate { Work(delegate { return HostsGuard.Restore() + " " + FlushDns(); }); });
        Label(updates, "Контроль hosts сообщает об изменениях. Причину изменения он не определяет: это может быть антивирус, другая программа или ручная правка. Восстановление выполняется только по твоей кнопке.");
        Label(updates, "IP-список определяет адреса, которые обрабатывает Zapret. Источник по умолчанию — V3nilla. После обновления останови и снова включи Zapret.");
        updates.Controls.Add(ipSource); ipSource.Visible = advancedMode; ipSource.TabStop = advancedMode;
        Button(updates, "Обновить IP-список", delegate
        {
            string url = ipSource.SelectedIndex == 0 ? NetworkFiles.VanillaIpset : NetworkFiles.FlowsealIpset;
            Work(delegate
            {
                byte[] bytes = NetworkFiles.Download(url, 16 * 1024 * 1024); int count = Files.ValidateIpset(bytes);
                string backup = Files.Replace(Path.Combine(folder, "lists", "ipset-all.txt"), bytes);
                return "IP-набор обновлён (" + count + " записей). Режим loaded. Перезапусти Zapret. Копия: " + backup;
            });
        });
        Button(updates, "Проверить обновления Zapret", delegate
        {
            Work(delegate
            {
                string current = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Path.Combine(folder, "service.bat")), "LOCAL_VERSION=([^\"\\r\\n]+)").Groups[1].Value;
                return "Установленная версия: " + current + ". Версия Flowseal: " + NetworkFiles.LatestVersion() + ".";
            });
        });
        Button(updates, "Открыть официальный релиз Flowseal", delegate { Process.Start("https://github.com/Flowseal/zapret-discord-youtube/releases/latest"); });

        FlowLayoutPanel tools = Page("Тестирование", false);
        Button(tools, "Диагностика автозапуска", delegate
        {
            log.Visible = true; notice.Visible = false;
            Work(delegate { return ZapretService.Diagnose(folder); });
        });
        Button(tools, "Скопировать журнал", delegate { if (log.TextLength > 0) Clipboard.SetText(log.Text); });
        Label(tools, "Проверь альты в своей сети. Выбор режима, ход проверки и лучший результат появятся в окне приложения. Для начала подходит обычная проверка HTTP и ping.");
        if (advancedMode) Button(tools, "Диагностика и исправления", delegate
        {
            if (MessageBox.Show(this, "Штатная диагностика может включать TCP timestamps, удалять конфликтующие службы и предлагать очистку кэша Discord. Открыть её окно?", "Диагностика", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            LaunchTool(false);
        });
        Button(tools, "Проверить альты", delegate { LaunchTool(true); });
        Button(tools, "Открыть результаты тестов", delegate
        {
            string path = Path.Combine(folder, "utils", "test results"); Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        });
        Label(tools, "После завершения тестов и закрытия этого окна лучший альт из отчёта будет подсвечен в списке. Если успешных проверок нет, рекомендация не появится.");
        tabs.AddPage("Темы", new ThemesPage(), true);
        LoadState(); Theme.Apply(this); tabs.Restyle(); tabs.SelectPage(initialPage);
        notice.ForeColor = Theme.Muted;
        toolTimer.Tick += delegate
        {
            if (tool != null && tool.HasExited) { tool.Dispose(); tool = null; toolTimer.Stop(); tabs.Enabled = true; Append("Окно инструмента закрыто. Результаты доступны в главном окне."); }
        };
        FormClosing += delegate(object s, FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            if (busy || (tool != null && !tool.HasExited))
            { e.Cancel = true; MessageBox.Show(this, "Дождись завершения операции или закрой штатное окно инструмента.", "Zapret GUI"); }
        };
        FormClosed += delegate { toolTimer.Dispose(); if (tool != null) tool.Dispose(); };
    }

    private static ComboBox Choice(params string[] options)
    {
        ComboBox box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        box.Items.AddRange(options); box.SelectedIndex = 0; return box;
    }
    private FlowLayoutPanel Page(string name, bool advancedOnly)
    {
        Panel page = new Panel();
        FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14) };
        page.Controls.Add(panel); tabs.AddPage(name, page, !advancedOnly || advancedMode);
        panel.Controls.Add(Theme.Caption(name, 20, true)); return panel;
    }
    private void Label(Control panel, string text)
    { panel.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(650, 0), Margin = new Padding(3, 8, 3, 5) }); }
    private void Button(Control panel, string text, Action action)
    {
        Button button = new SoftButton { Text = text, AutoSize = true, MinimumSize = new Size(320, 34), Margin = new Padding(3, 4, 3, 4) };
        button.Click += delegate { try { action(); } catch (Exception e) { ShowError(e); } }; panel.Controls.Add(button);
    }
    private void Append(string value)
    {
        log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + value + "\r\n");
        string summary = value.Split(new[] { "Копия:", "Резервная копия:", "Предыдущая версия:" }, StringSplitOptions.None)[0].Trim();
        notice.Text = summary.Length > 220 ? summary.Substring(0, 217) + "…" : summary;
    }
    private void ShowError(Exception error)
    {
        Append(error.Message);
        string report = error.Data["HostsReport"] as string;
        if (report != null)
        {
            log.Visible = true; notice.Visible = false;
            Append(report); log.SelectionStart = log.TextLength; log.ScrollToCaret();
        }
        string manual = error.Data["ManualHosts"] as string;
        string message = error.Message;
        if (manual != null)
        {
            string instructions = HostsManual.Instructions(manual) + HostsManual.OpenFolders(manual);
            log.Visible = true; notice.Visible = false; Append(instructions);
            message = "Автоматически заменить hosts не удалось: файл занят, недостаточно прав или запись блокирует защита.\r\n\r\n" + instructions;
        }
        MessageBox.Show(this, message, "Операция не завершена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    private void Work(Func<string> operation)
    {
        if (busy) return;
        busy = true; tabs.Enabled = false; UseWaitCursor = true; Append("Выполняется…");
        BackgroundWorker worker = new BackgroundWorker();
        worker.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = operation(); };
        worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
        {
            if (IsDisposed || Disposing) { worker.Dispose(); return; }
            busy = false; tabs.Enabled = true; UseWaitCursor = false;
            if (e.Error != null) ShowError(e.Error); else
            {
                string result = (string)e.Result; Append(result);
                if (result.StartsWith("РУЧНАЯ ЗАМЕНА HOSTS"))
                { log.Visible = true; notice.Visible = false; MessageBox.Show(this, result, "Hosts для ручной замены", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            }
            try { LoadState(); } catch (Exception error) { Append(error.Message); }
            worker.Dispose();
        };
        worker.RunWorkerAsync();
    }
    private void LoadState()
    {
        string settings = Path.Combine(folder, "utils", "game_filter.enabled");
        game.SelectedIndex = 0;
        if (File.Exists(settings))
            foreach (string line in File.ReadAllLines(settings))
            {
                string[] pair = line.Trim().Split(new[] { '=' }, 2);
                if (pair[0] == "mode" && pair.Length == 2) game.SelectedIndex = Array.IndexOf(new[] { "disabled", "all", "tcp", "udp" }, pair[1]);
                else if (pair[0] == "tcp" && pair.Length == 2) tcp.Text = pair[1];
                else if (pair[0] == "udp" && pair.Length == 2) udp.Text = pair[1];
                else if (pair.Length == 1 && pair[0] == "all") game.SelectedIndex = 1;
                else if (pair.Length == 1 && pair[0] == "tcp") game.SelectedIndex = 2;
                else if (pair.Length == 1 && pair[0] == "udp") game.SelectedIndex = 3;
            }
        autoUpdate.Checked = File.Exists(Path.Combine(folder, "utils", "check_updates.enabled"));
        string list = Path.Combine(folder, "lists", "ipset-all.txt");
        string value = File.Exists(list) ? File.ReadAllText(list) : "203.0.113.113/32";
        ipset.SelectedIndex = string.IsNullOrWhiteSpace(value) ? 2 : value.Contains("203.0.113.113/32") ? 1 : 0;
    }
    private void SaveGame()
    {
        string tcpPorts = tcp.Text.Replace(" ", ""), udpPorts = udp.Text.Replace(" ", "");
        if (!Files.ValidPorts(tcpPorts) || !Files.ValidPorts(udpPorts) || game.SelectedIndex < 0) throw new InvalidDataException("Проверь порты: например 1024-1934,1936-65535. Допустимы значения 1–65535.");
        string value = "mode=" + new[] { "disabled", "all", "tcp", "udp" }[game.SelectedIndex] + "\r\ntcp=" + tcpPorts + "\r\nudp=" + udpPorts + "\r\n";
        Files.Replace(Path.Combine(folder, "utils", "game_filter.enabled"), Encoding.ASCII.GetBytes(value));
        Append("Game Filter сохранён. Перезапусти Zapret для применения.");
    }
    private void SaveIpsetMode()
    {
        string list = Path.Combine(folder, "lists", "ipset-all.txt"), backup = list + ".backup";
        string current = File.Exists(list) ? File.ReadAllText(list) : "";
        bool loaded = !string.IsNullOrWhiteSpace(current) && !current.Contains("203.0.113.113/32");
        if (ipset.SelectedIndex == 0)
        {
            if (!loaded)
            {
                if (!File.Exists(backup)) throw new IOException("Нет сохранённого списка. Сначала обнови IPSet List.");
                byte[] bytes = File.ReadAllBytes(backup); Files.ValidateIpset(bytes); Files.Replace(list, bytes);
            }
        }
        else
        {
            if (loaded) Files.Replace(backup, File.ReadAllBytes(list));
            Files.Replace(list, ipset.SelectedIndex == 1 ? Encoding.ASCII.GetBytes("203.0.113.113/32\r\n") : new byte[0]);
        }
        Append("Режим IPSet сохранён. Перезапусти Zapret для применения.");
    }
    private static string HostsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "etc", "hosts"); } }
    private void UpdateHosts(string local)
    {
        Work(delegate
        {
            if (local != null && new FileInfo(local).Length > 8 * 1024 * 1024) throw new IOException("Слишком большой hosts-файл.");
            byte[] bytes = local == null ? NetworkFiles.Download(NetworkFiles.HostsUrl, 8 * 1024 * 1024) : File.ReadAllBytes(local);
            int count = Files.ValidateHosts(bytes);
            byte[] applied = new UTF8Encoding(false).GetBytes(Files.Text(bytes));
            string backup;
            try { backup = HostsWriter.Replace(HostsPath, applied, HostsWriter.ReadHash(HostsPath)); }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException)) throw;
                try { error.Data["ManualHosts"] = HostsManual.Prepare(applied); }
                catch (Exception saveError) { throw new IOException(error.Message + "\r\nНе удалось сохранить файл для ручной замены: " + saveError.Message, error); }
                throw;
            }
            try { HostsGuard.Remember(applied); } catch (Exception error) { return "Hosts применён; сохранить копию для контроля не удалось: " + error.Message; }
            string dns = FlushDns();
            return "Hosts заменён: " + count + " записей. Копия: " + backup + ". " + dns;
        });
    }
    private void RestoreHosts()
    {
        using (OpenFileDialog dialog = new OpenFileDialog { Title = "Выбери резервную копию hosts", InitialDirectory = Path.GetDirectoryName(HostsPath), Filter = "Копии Zapret GUI|hosts.zapretgui.*.bak" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string path = dialog.FileName;
            Work(delegate
            {
                byte[] bytes = File.ReadAllBytes(path);
                // Original hosts can legitimately contain only comments or be empty.
                if (bytes.Length > 8 * 1024 * 1024) throw new IOException("Слишком большая резервная копия.");
                string backup = HostsWriter.Replace(HostsPath, bytes, HostsWriter.ReadHash(HostsPath));
                HostsGuard.Remember(bytes);
                return "Hosts восстановлен. Предыдущая версия: " + backup + ". " + FlushDns();
            });
        }
    }
    private static string FlushDns()
    {
        try
        {
            using (Process process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns") { UseShellExecute = false, CreateNoWindow = true }))
                return process.WaitForExit(5000) && process.ExitCode == 0 ? "Кэш DNS очищен." : "Кэш DNS автоматически очистить не удалось.";
        }
        catch { return "Кэш DNS автоматически очистить не удалось."; }
    }
    private static string Status()
    {
        try { using (ServiceController service = new ServiceController("zapret")) return "Состояние службы zapret: " + service.Status; }
        catch (InvalidOperationException) { return "Служба zapret не установлена."; }
    }
    private void ServiceAction(bool start)
    {
        if (start && !stopOwn()) return;
        Work(delegate
        {
            using (ServiceController service = new ServiceController("zapret"))
            {
                service.Refresh();
                if (start && service.Status == ServiceControllerStatus.Stopped) service.Start();
                else if (!start && service.Status != ServiceControllerStatus.Stopped) service.Stop();
                service.WaitForStatus(start ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            }
            return Status();
        });
    }
    private void LaunchTool(bool tests)
    {
        if (!stopOwn()) return;
        if (tests)
        {
            using (TestWindow dialog = new TestWindow(folder, strategy, advancedMode)) dialog.ShowDialog(this);
            Append("Проверка закрыта. Вернись на главный экран: лучший альт из свежего отчёта будет выделен.");
            return;
        }
        tool = ServiceBridge.Launch(folder, "service_diagnostics", strategy, false);
        tabs.Enabled = false; toolTimer.Start(); Append("Открыт штатный инструмент. Заверши работу в его окне.");
    }
}
