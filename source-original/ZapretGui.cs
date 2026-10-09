// C# 5 / .NET Framework: built locally using the Windows Framework compiler.
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Security.Principal;
using System.Reflection;
using Microsoft.Win32;

[assembly: AssemblyVersion("0.6.0.0")]
[assembly: AssemblyFileVersion("0.6.0.0")]

internal static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [STAThread]
    private static void Main(string[] args)
    {
        try { SetCurrentProcessExplicitAppUserModelID("ZapretGUI.Desktop.ZT"); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (args.Contains("--prepare-uninstall")) { Maintenance.BeforeUninstall(); return; }
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" }); }
                catch (Win32Exception error) { if (error.NativeErrorCode != 1223) throw; }
                return;
            }
        }
        catch (Exception error) { MessageBox.Show(error.Message, "Zapret GUI", MessageBoxButtons.OK, MessageBoxIcon.Error); Environment.ExitCode = 1; return; }
        bool first;
        using (Mutex mutex = new Mutex(true, @"Local\ZapretSimpleGui-54DA0A3E", out first))
        {
            if (!first)
            {
                MessageBox.Show("Оболочка уже открыта. Открой её через значок в трее рядом с часами.", "Zapret GUI");
                return;
            }
            try { Application.Run(new MainWindow()); }
            catch (Exception error) { MessageBox.Show(error.Message, "Не удалось открыть Zapret GUI", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { mutex.ReleaseMutex(); }
        }
    }
}

internal sealed class Strategy
{
    public readonly string FilePath;
    public Strategy(string path) { FilePath = path; }
    public override string ToString()
    {
        string name = Path.GetFileNameWithoutExtension(FilePath);
        return name.Equals("general", StringComparison.OrdinalIgnoreCase) ? "Основной альт" : Regex.Replace(name, @"^general\s*", "", RegexOptions.IgnoreCase).Trim('(', ')').Replace("ALT", "Альт");
    }
}

internal sealed class MainWindow : SessionForm
{
    private readonly TextBox folderBox = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ListBox strategies = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label status = new Label { AutoSize = true, Text = "Выбери папку Zapret." };
    private readonly Button browse = new SoftButton { Text = "Выбрать папку…", AutoSize = true };
    private readonly Button refresh = new SoftButton { Text = "Обновить список", AutoSize = true };
    private readonly Button find = new SoftButton { Text = "Найти Zapret", AutoSize = true };
    private readonly Button features = new SoftButton { Text = "Служба и настройки…", AutoSize = true };
    private readonly BackgroundWorker search = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
    private readonly Button start = new SoftButton { Text = "Запустить", AutoSize = true };
    private readonly Button stop = new SoftButton { Text = "Остановить", AutoSize = true, Enabled = false };
    private readonly Button showLog = new SoftButton { Text = "Журнал запуска", AutoSize = true };
    private readonly TextBox eventsBox = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 800 };
    private readonly string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretSimpleGui");
    private string folder = "";
    private string logFile = "";
    private bool busy, realExit, featuresOpen;
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly Button startupButton = new SoftButton { Text = "Включить автозагрузку", AutoSize = true };
    private ServiceState serviceState = new ServiceState();
    private string recommended = "";
    private bool checkingUpdate;
    private readonly ThemeSwitch themeSwitch = new ThemeSwitch();
    private DateTime hostsChecked = DateTime.MinValue;
    private readonly Label hostsHint = Theme.Caption("Крестик сворачивает окно в трей. Чтобы отключить Zapret, нажми «Остановить».", 9, false);
    private readonly Label selectedHeading = Theme.Caption("Выбери альт", 23, true);
    private readonly CheckBox animations = new SoftToggle { Text = "Плавные анимации", AutoSize = false, Width = 180, Height = 50, Checked = true };
    private readonly CheckBox advanced = new SoftToggle { Text = "Advanced mode\r\nВсе настройки", AutoSize = false, Width = 180, Height = 88 };
    private readonly Button advancedSettings = new SoftButton { Text = "Технические настройки", Width = 184, Height = 56 };
    private readonly Panel technical = new Panel { Dock = DockStyle.Fill, Height = 164, MinimumSize = new Size(0, 164) };
    private readonly Label recommendationHint = Theme.Caption("Альт — набор настроек обхода блокировок. Если доступ не появился, попробуй другой или запусти тестирование.", 10, false);
    private readonly ToolTip hints = new ToolTip { AutoPopDelay = 15000, InitialDelay = 500 };

    public MainWindow()
    {
        Text = "Zapret GUI — простая оболочка";
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1060, 810);
        MinimumSize = new Size(930, 740);
        StartPosition = FormStartPosition.CenterScreen;

        BuildInterface();

        browse.Click += delegate { ChooseFolder(); };
        features.Click += delegate { OpenFeatures("Списки и обновления"); };
        advancedSettings.Click += delegate { OpenFeatures("Служба"); };
        themeSwitch.CheckedChanged += delegate { Theme.Switch(themeSwitch.Checked); SaveSettings(); Poll(); };
        advanced.CheckedChanged += delegate { ApplyMode(); SaveSettings(); };
        strategies.DrawMode = DrawMode.OwnerDrawFixed;
        strategies.ItemHeight = 58;
        strategies.DrawItem += delegate(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Strategy item = (Strategy)strategies.Items[e.Index];
            bool best = Path.GetFileName(item.FilePath).Equals(recommended, StringComparison.OrdinalIgnoreCase);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (Brush background = new SolidBrush(strategies.BackColor)) e.Graphics.FillRectangle(background, e.Bounds);
            if (best || selected)
                using (System.Drawing.Drawing2D.GraphicsPath path = Theme.Round(new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 5, e.Bounds.Height - 4), 9))
                using (Brush fill = new SolidBrush(best ? Theme.Selected : Theme.Raised))
                { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.FillPath(fill, path); }
            Rectangle line = new Rectangle(e.Bounds.X + 16, e.Bounds.Y + 7, e.Bounds.Width - 24, 24);
            TextRenderer.DrawText(e.Graphics, item.ToString() + (best ? "  ·  рекомендован" : ""), Font, line,
                best ? Theme.Accent : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            line.Y += 24; line.Height = 20;
            string detail = advanced.Checked ? Path.GetFileName(item.FilePath) : best ? "Лучший результат последнего теста в твоей сети" : selected ? "Выбран для следующего запуска" : "Нажми, чтобы выбрать этот вариант";
            TextRenderer.DrawText(e.Graphics, detail, Font, line, Theme.Muted, TextFormatFlags.EndEllipsis);
            if (selected) using (Pen accent = new Pen(Theme.Accent, 3)) { accent.StartCap = accent.EndCap = System.Drawing.Drawing2D.LineCap.Round; e.Graphics.DrawLine(accent, e.Bounds.X + 5, e.Bounds.Y + 12, e.Bounds.X + 5, e.Bounds.Bottom - 12); }
            e.DrawFocusRectangle();
        };
        find.Click += delegate { if (search.IsBusy) { search.CancelAsync(); find.Enabled = false; find.Text = "Останавливаю…"; } else BeginSearch(); };
        search.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = Discovery.Scan(search); };
        search.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e) { status.Text = "Поиск Zapret: проверено папок " + e.ProgressPercentage; };
        search.RunWorkerCompleted += SearchFinished;
        refresh.Click += delegate { if (folder.Length > 0) LoadFolder(folder, SelectedFile()); };
        start.Click += delegate { StartStrategy(); };
        stop.Click += delegate { StopStrategy(); };
        showLog.Click += delegate { OpenLog(); };
        strategies.SelectedIndexChanged += delegate { selectedHeading.Text = strategies.SelectedItem == null ? "Выбери альт" : strategies.SelectedItem.ToString(); UpdateButtons(); SaveSettings(); };
        animations.CheckedChanged += delegate { Motion.Enabled = animations.Checked; SaveSettings(); };
        timer.Tick += delegate { Poll(); };
        FormClosing += OnClosing;
        FormClosed += delegate { timer.Stop(); timer.Dispose(); hints.Dispose(); tray.Visible = false; Icon trayIcon = tray.Icon; tray.Dispose(); if (trayIcon != null) trayIcon.Dispose(); };
        ContextMenuStrip trayMenu = new ContextMenuStrip { BackColor = Theme.Panel, ForeColor = Theme.Text, Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) };
        trayMenu.Items.Add("Открыть Zapret GUI", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
        trayMenu.Items.Add("Остановить Zapret", null, delegate { StopStrategy(); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Завершить GUI (служба продолжит работу)", null, delegate { realExit = true; Close(); });
        Action refreshTray = delegate { trayMenu.BackColor = Theme.Panel; trayMenu.ForeColor = Theme.Text; foreach (ToolStripItem item in trayMenu.Items) item.ForeColor = Theme.Text; trayMenu.Invalidate(); };
        Theme.Changed += refreshTray; FormClosed += delegate { Theme.Changed -= refreshTray; };
        tray.Icon = Brand.CreateIcon(32); tray.Text = "Zapret GUI"; tray.ContextMenuStrip = trayMenu; tray.Visible = true;
        tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
        startupButton.Click += delegate
        {
            bool enabled = !serviceState.Automatic;
            RunOperation(delegate { ZapretService.SetAutomatic(folder, enabled); return enabled ? "Автозагрузка включена: Zapret запустится вместе с Windows." : "Автозагрузка отключена. Текущий запуск продолжает работать."; });
        };
        Shown += delegate { RestoreSettings(); Poll(); timer.Start(); };
        ApplyMode();
        UpdateButtons();
    }

    private void BuildInterface()
    {
        TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        TableLayoutPanel sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 24, 12, 14), RowCount = 4, ColumnCount = 1 };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 200)); sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 142)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
        FlowLayoutPanel brand = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        PictureBox logo = new PictureBox { Image = Brand.CreateImage(96), Size = new Size(80, 80), SizeMode = PictureBoxSizeMode.Zoom };
        logo.Disposed += delegate { logo.Image.Dispose(); };
        brand.Controls.Add(themeSwitch); brand.Controls.Add(logo); brand.Controls.Add(Theme.Caption("Zapret GUI", 18, true));
        sidebar.Controls.Add(brand, 0, 0);
        FlowLayoutPanel navigation = new FlowLayoutPanel { AutoScroll = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Button home = new SoftButton { Text = "Подключение", Width = 184, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
        home.Click += delegate { strategies.Focus(); };
        features.Text = "Списки и обновления"; features.AutoSize = false; features.Size = new Size(184, 48); features.TextAlign = ContentAlignment.MiddleLeft;
        Button tests = new SoftButton { Text = "Тестирование", Width = 184, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
        tests.Click += delegate { if (folder.Length > 0 && !busy) OpenFeatures("Тестирование"); };
        Button creators = new SoftButton { Text = "Создатели", Width = 184, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
        creators.Click += delegate { using (CreatorsWindow dialog = new CreatorsWindow()) dialog.ShowDialog(this); };
        navigation.Controls.AddRange(new Control[] { home, features, tests, creators, advancedSettings });
        sidebar.Controls.Add(navigation, 0, 1); FlowLayoutPanel preferences = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        preferences.Controls.Add(advanced); preferences.Controls.Add(animations); sidebar.Controls.Add(preferences, 0, 2);
        FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        footer.Controls.Add(Theme.Caption("Версия 0.6", 9, false));
        Label help = Theme.Caption("Чтобы подобрать лучший альт, открой «Тестирование» и нажми «Проверить альты».", 9, false); help.MaximumSize = new Size(178, 0); footer.Controls.Add(help); sidebar.Controls.Add(footer, 0, 3);
        shell.Controls.Add(sidebar, 0, 0);
        TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 5, ColumnCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 218));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        FlowLayoutPanel heading = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        heading.Controls.Add(selectedHeading); heading.Controls.Add(Theme.Caption("Выбранный профиль подключения · Zapret GUI", 10, false)); body.Controls.Add(heading, 0, 0);
        Card connection = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) };
        FlowLayoutPanel connectionRows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        status.Font = new Font("Segoe UI", 14F, FontStyle.Bold); status.MaximumSize = new Size(680, 0); status.Margin = new Padding(0, 0, 0, 12);
        connectionRows.Controls.Add(Theme.Caption("СОСТОЯНИЕ ПОДКЛЮЧЕНИЯ", 9, false));
        connectionRows.Controls.Add(status);
        FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        start.Text = "Включить Zapret"; start.MinimumSize = new Size(152, 44); stop.MinimumSize = new Size(110, 44); startupButton.MinimumSize = new Size(208, 44);
        actions.Controls.AddRange(new Control[] { start, stop, startupButton }); connectionRows.Controls.Add(actions);
        connectionRows.Controls.Add(Theme.Caption("Автозагрузка включает Zapret вместе с Windows. Сначала запусти выбранный альт.", 9, false));
        connection.Controls.Add(connectionRows); body.Controls.Add(connection, 0, 1);
        Card profile = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) };
        TableLayoutPanel profileRows = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        profileRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); profileRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        profileRows.RowStyles.Add(new RowStyle(SizeType.AutoSize)); profileRows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        profileRows.Controls.Add(Theme.Caption("Способ подключения", 13, true), 0, 0); recommendationHint.Dock = DockStyle.Fill;
        profileRows.Controls.Add(recommendationHint, 0, 1); profileRows.Controls.Add(strategies, 0, 2); profile.Controls.Add(profileRows); body.Controls.Add(profile, 0, 2);
        TableLayoutPanel details = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); details.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        TableLayoutPanel folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(folderBox, 0, 0); folderRow.Controls.Add(browse, 1, 0); details.Controls.Add(folderRow, 0, 0);
        FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill }; tools.Controls.AddRange(new Control[] { find, refresh, showLog });
        details.Controls.Add(tools, 0, 1); details.Controls.Add(eventsBox, 0, 2); technical.Controls.Add(details); body.Controls.Add(technical, 0, 3);
        body.Controls.Add(hostsHint, 0, 4); hostsHint.Cursor = Cursors.Hand; hostsHint.Click += delegate { OpenFeatures("Списки и обновления"); };
        shell.Controls.Add(body, 1, 0); Controls.Add(shell); Theme.Apply(this);
        shell.Tag = body.Tag = heading.Tag = "canvas"; shell.BackColor = body.BackColor = heading.BackColor = Theme.Background;
        Theme.Primary(start); home.Tag = "selected"; home.BackColor = Theme.Selected; home.ForeColor = Theme.Accent;
        hints.SetToolTip(advanced, "Показать порты, fake-файлы, управление службами, путь к сборке и журнал. Настройки Zapret при переключении режима не меняются.");
        hints.SetToolTip(startupButton, "Автозапуск службы Zapret при включении компьютера. Само окно GUI открывать не потребуется.");
        hints.SetToolTip(strategies, "Перед сменой альта останови Zapret. Зелёным отмечен лучший результат последнего теста.");
        hints.SetToolTip(features, "Обновление hosts и IP-списков, восстановление резервной копии, проверка версии.");
    }

    private void ApplyMode()
    {
        technical.Visible = advancedSettings.Visible = advanced.Checked;
        advancedSettings.TabStop = advanced.Checked;
        strategies.Invalidate();
    }

    private void OpenFeatures(string page)
    {
        if (featuresOpen || busy || search.IsBusy || folder.Length == 0) return;
        featuresOpen = true;
        try { using (FeaturesWindow dialog = new FeaturesWindow(folder, SelectedFile(), delegate { return !busy; }, advanced.Checked, page)) dialog.ShowDialog(this); }
        finally { featuresOpen = false; }
        LoadFolder(folder, SelectedFile()); Poll();
    }

    private string SelectedFile()
    {
        Strategy selected = strategies.SelectedItem as Strategy;
        return selected == null ? "" : Path.GetFileName(selected.FilePath);
    }

    private void Note(string message)
    {
        if (eventsBox.TextLength > 20000) eventsBox.Clear();
        eventsBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
    }

    private void Error(string message)
    {
        Note(message);
        MessageBox.Show(this, message, "Zapret GUI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ChooseFolder()
    {
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "Выбери распакованную папку Zapret, где находятся general*.bat, service.bat и папка bin.";
            dialog.ShowNewFolderButton = false;
            if (Directory.Exists(folder)) dialog.SelectedPath = folder;
            if (dialog.ShowDialog(this) == DialogResult.OK) LoadFolder(dialog.SelectedPath, "");
        }
    }

    private void LoadFolder(string candidate, string preferred)
    {
        try
        {
            candidate = Path.GetFullPath(candidate);
            string[] files = Directory.GetFiles(candidate, "general*.bat", SearchOption.TopDirectoryOnly);
            if (!File.Exists(Path.Combine(candidate, "bin", "winws.exe")) || !File.Exists(Path.Combine(candidate, "service.bat")) || files.Length == 0)
                throw new InvalidOperationException("Это не папка сборки Flowseal. Нужны general*.bat, service.bat и bin\\winws.exe.");
            files = files.OrderBy(f => Path.GetFileName(f).Equals("general.bat", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => Regex.Replace(Path.GetFileName(f), @"\d+", m => m.Value.PadLeft(12, '0')), StringComparer.OrdinalIgnoreCase).ToArray();
            folder = candidate;
            folderBox.Text = folder;
            strategies.BeginUpdate();
            try
            {
                strategies.Items.Clear();
                foreach (string file in files) strategies.Items.Add(new Strategy(file));
                int index = Array.FindIndex(files, f => Path.GetFileName(f).Equals(preferred, StringComparison.OrdinalIgnoreCase));
                if (index < 0) index = Array.FindIndex(files, f => Path.GetFileName(f).Equals("general.bat", StringComparison.OrdinalIgnoreCase));
                strategies.SelectedIndex = Math.Max(index, 0);
            }
            finally { strategies.EndUpdate(); }
            status.Text = "Готово к запуску. Стратегий: " + files.Length;
            status.ForeColor = Theme.Text;
            Note("Подключена папка: " + folder);
            ReadRecommendation();
            SaveSettings();
            UpdateButtons();
        }
        catch (Exception error) { Error(error.Message); }
    }

    private void RestoreSettings()
    {
        try { themeSwitch.Checked = (string)XElement.Load(Path.Combine(dataFolder, "settings.xml")).Element("theme") == "light"; } catch { }
        try { animations.Checked = (bool?)XElement.Load(Path.Combine(dataFolder, "settings.xml")).Element("animations") ?? true; Motion.Enabled = animations.Checked; } catch { }
        try { advanced.Checked = (bool?)XElement.Load(Path.Combine(dataFolder, "settings.xml")).Element("advanced") ?? false; } catch { }
        string embedded = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zapret");
        if (Discovery.IsBundle(embedded))
        {
            string preferred = "";
            try { preferred = (string)XElement.Load(Path.Combine(dataFolder, "settings.xml")).Element("strategy") ?? ""; }
            catch { }
            LoadFolder(embedded, preferred);
            return;
        }
        string settingsPath = Path.Combine(dataFolder, "settings.xml");
        if (File.Exists(settingsPath))
        {
            try
            {
                XElement settings = XElement.Load(settingsPath);
                string savedFolder = (string)settings.Element("folder") ?? "";
                if (Discovery.IsBundle(savedFolder)) { LoadFolder(savedFolder, (string)settings.Element("strategy") ?? ""); return; }
            }
            catch (Exception error) { Note("Настройки не прочитаны: " + error.Message); }
        }
        BeginSearch();
    }

    private void BeginSearch()
    {
        if (busy || serviceState.Running || search.IsBusy) return;
        status.Text = "Ищу папку с альтами Zapret…";
        status.ForeColor = Theme.Text;
        find.Text = "Отменить поиск";
        search.RunWorkerAsync();
        UpdateButtons();
    }

    private void SearchFinished(object sender, RunWorkerCompletedEventArgs args)
    {
        if (IsDisposed || Disposing) return;
        find.Text = "Найти Zapret";
        find.Enabled = true;
        UpdateButtons();
        if (args.Error != null) { Error("Не удалось закончить поиск: " + args.Error.Message); return; }
        Discovery.Result result = args.Result as Discovery.Result;
        if (result == null) return;
        Note("Поиск: проверено папок " + result.Checked + ", найдено сборок " + result.Folders.Count + ". " + result.Reason);
        if (result.Folders.Count == 0)
        {
            status.Text = "Папка не найдена. Можно выбрать её вручную.";
            return;
        }
        string picked = result.Folders[0];
        if (result.Folders.Count > 1)
        {
            using (Form dialog = new SessionForm { Text = "Найдено несколько сборок Zapret", Size = new Size(720, 350), MinimumSize = new Size(480, 250), StartPosition = FormStartPosition.CenterParent, Font = Font })
            {
                ListBox matches = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
                foreach (string path in result.Folders) matches.Items.Add(path);
                matches.SelectedIndex = 0;
                Button use = new SoftButton { Text = "Использовать выбранную", Dock = DockStyle.Bottom, Height = 42, DialogResult = DialogResult.OK };
                dialog.Controls.Add(matches); dialog.Controls.Add(use); dialog.AcceptButton = use; Theme.Apply(dialog);
                if (dialog.ShowDialog(this) != DialogResult.OK) { status.Text = "Выбор папки отменён."; return; }
                picked = (string)matches.SelectedItem;
            }
        }
        LoadFolder(picked, "");
    }

    private void SaveSettings()
    {
        if (folder.Length == 0 || strategies.SelectedItem == null) return;
        try
        {
            Directory.CreateDirectory(dataFolder);
            string path = Path.Combine(dataFolder, "settings.xml");
            new XElement("settings", new XElement("folder", folder), new XElement("strategy", SelectedFile()), new XElement("advanced", advanced.Checked), new XElement("animations", animations.Checked), new XElement("theme", themeSwitch.Checked ? "light" : "dark")).Save(path + ".tmp");
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
            else File.Move(path + ".tmp", path);
        }
        catch (Exception error) { Note("Не удалось сохранить настройки: " + error.Message); }
    }

    private void UpdateButtons()
    {
        bool idle = !busy && !featuresOpen && !search.IsBusy;
        start.Enabled = idle && !serviceState.Running && strategies.SelectedItem != null;
        stop.Enabled = idle && serviceState.Owned && serviceState.Running;
        startupButton.Enabled = idle && serviceState.Owned;
        startupButton.Text = serviceState.Automatic ? "Отключить автозагрузку" : "Включить автозагрузку";
        find.Enabled = idle && !serviceState.Running;
        features.Enabled = advancedSettings.Enabled = idle && folder.Length > 0;
        browse.Enabled = refresh.Enabled = strategies.Enabled = idle && (!serviceState.Running || !serviceState.Owned);
        showLog.Enabled = File.Exists(logFile);
    }

    private void ReadRecommendation()
    {
        recommended = "";
        try
        {
            string reports = Path.Combine(folder, "utils", "test results");
            if (!Directory.Exists(reports)) return;
            string latest = Directory.GetFiles(reports, "test_results_*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (latest == null) return;
            string content = File.ReadAllText(latest);
            Match best = Regex.Match(content, @"(?m)^Best strategy:\s*(.+?)\s*$");
            if (!best.Success) return;
            string candidate = best.Groups[1].Value.Trim();
            Match score = Regex.Match(content, @"(?m)^" + Regex.Escape(candidate) + @"\s*:\s*(?:HTTP )?OK:\s*(\d+)");
            if (!score.Success || int.Parse(score.Groups[1].Value) < 1) { Note("Последний тест не выявил стратегию с успешными проверками."); return; }
            if (strategies.Items.Cast<Strategy>().Any(s => Path.GetFileName(s.FilePath).Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            { recommended = candidate; Note("По тесту от " + File.GetLastWriteTime(latest).ToString("g") + " рекомендуется " + candidate + "."); }
        }
        catch (Exception error) { Note("Результат тестов не прочитан: " + error.Message); }
        finally { recommendationHint.Text = recommended.Length == 0 ? "Альт — набор настроек обхода блокировок. Если доступ не появился, попробуй другой или запусти тестирование." : "По последнему тесту рекомендуется " + recommended + ". Он выделен мятным цветом в списке."; strategies.Invalidate(); }
    }

    private void StartStrategy()
    {
        Strategy selected = strategies.SelectedItem as Strategy;
        if (selected == null || busy) return;
        string root = folder, file = Path.GetFileName(selected.FilePath);
        bool automatic = serviceState.Automatic;
        RunOperation(delegate
        {
            ZapretService.EnsureNoForeignService(root);
            foreach (Process process in Process.GetProcessesByName("winws"))
                using (process) throw new InvalidOperationException("Уже работает winws.exe. Останови прежний запуск Zapret перед включением выбранного альта.");
            string output = ServiceBridge.Run(root, "service_install", file, automatic);
            using (ServiceController service = new ServiceController("zapret"))
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            ServiceState state = ZapretService.Read(root);
            if (!state.Owned || !state.Running) throw new IOException("Служба не запустилась. Журнал штатного скрипта:\r\n" + output);
            return "Запущена служба: " + file + "\r\n" + output;
        });
        CheckUpdateInBackground();
    }

    private void RunOperation(Func<string> operation)
    {
        if (busy || featuresOpen || search.IsBusy) return;
        busy = true; UpdateButtons(); status.Text = "Выполняется…";
        BackgroundWorker worker = new BackgroundWorker();
        worker.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = operation(); };
        worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
        {
            busy = false;
            if (!IsDisposed && !Disposing)
            {
                string text = e.Error == null ? (string)e.Result : e.Error.Message;
                try { Directory.CreateDirectory(dataFolder); logFile = Path.Combine(dataFolder, "last-launch.log"); File.WriteAllText(logFile, text, Encoding.UTF8); } catch { }
                if (e.Error != null) Error(text); else Note(text);
                Poll();
            }
            worker.Dispose();
        };
        worker.RunWorkerAsync();
    }

    private void CheckUpdateInBackground()
    {
        if (checkingUpdate || !File.Exists(Path.Combine(folder, "utils", "check_updates.enabled"))) return;
        checkingUpdate = true;
        BackgroundWorker update = new BackgroundWorker();
        string servicePath = Path.Combine(folder, "service.bat");
        update.DoWork += delegate(object sender, DoWorkEventArgs e)
        {
            string installed = Regex.Match(File.ReadAllText(servicePath), "LOCAL_VERSION=([^\"\\r\\n]+)").Groups[1].Value;
            string latest = NetworkFiles.LatestVersion();
            e.Result = latest == installed ? "" : "Доступна версия Zapret " + latest + ". Служба и настройки → Обновления.";
        };
        update.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
        {
            checkingUpdate = false;
            if (!IsDisposed && !Disposing)
            {
                if (e.Error != null) Note("Проверка обновлений недоступна; Zapret продолжает работать.");
                else if (!string.IsNullOrEmpty((string)e.Result)) Note((string)e.Result);
            }
            update.Dispose();
        };
        update.RunWorkerAsync();
    }

    private void Poll()
    {
        if ((DateTime.UtcNow - hostsChecked).TotalSeconds > 30)
        {
            hostsChecked = DateTime.UtcNow;
            try { string changed = HostsGuard.Check(); hostsHint.Text = changed.Length == 0 ? "Крестик сворачивает окно в трей. Чтобы отключить Zapret, нажми «Остановить»." : changed; hostsHint.ForeColor = changed.Length == 0 ? Theme.Muted : Theme.Error; }
            catch { hostsHint.Text = "Не удалось проверить hosts. Открой «Списки и обновления»."; }
        }
        if (busy || search.IsBusy) return;
        try
        {
            serviceState = ZapretService.Read(folder);
            status.Text = serviceState.Running ? "Zapret работает" : "Zapret выключен";
            if (serviceState.Exists && !serviceState.Owned) status.Text += " · запущен из другой папки";
            else if (serviceState.Owned) status.Text += serviceState.Automatic ? " · автозагрузка включена" : " · автозагрузка выключена";
            status.ForeColor = serviceState.Running ? Theme.Accent : Theme.Text;
            tray.Text = serviceState.Running ? "Zapret GUI — Zapret работает" : "Zapret GUI — Zapret остановлен";
        }
        catch (Exception error) { status.Text = "Статус недоступен: " + error.Message; }
        UpdateButtons();
    }

    private void StopStrategy()
    {
        string root = folder;
        RunOperation(delegate { ZapretService.Stop(root); return "Служба Zapret остановлена. Настройка автозагрузки сохранена."; });
    }

    private void OpenLog()
    {
        try { if (File.Exists(logFile)) Process.Start(Path.Combine(Environment.SystemDirectory, "notepad.exe"), "\"" + logFile + "\""); }
        catch (Exception error) { Error(error.Message); }
    }

    private void OnClosing(object sender, FormClosingEventArgs args)
    {
        if (args.CloseReason == CloseReason.WindowsShutDown || args.CloseReason == CloseReason.TaskManagerClosing) return;
        if (!realExit && args.CloseReason == CloseReason.UserClosing)
        { args.Cancel = true; Hide(); return; }
        if (busy || featuresOpen) { args.Cancel = true; realExit = false; Error("Дождись завершения текущей операции."); return; }
        if (search.IsBusy) search.CancelAsync();
    }
}

// Never veto shutdown or open dialogs during session termination. State is saved
// as it changes; the Windows service manager owns the engine lifetime.
internal class SessionForm : Form
{
    public SessionForm() { Icon = Brand.CreateIcon(32); BackColor = Theme.Background; ForeColor = Theme.Text; }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Motion.Show(this); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }
    protected override void Dispose(bool disposing)
    {
        Icon owned = disposing ? Icon : null;
        base.Dispose(disposing);
        if (owned != null) owned.Dispose();
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0011) { message.Result = new IntPtr(1); return; }
        if (message.Msg == 0x0016 && message.WParam != IntPtr.Zero) { Environment.Exit(0); return; }
        base.WndProc(ref message);
    }
}
