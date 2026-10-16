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

[assembly: AssemblyVersion("0.9.15.0")]
[assembly: AssemblyFileVersion("0.9.15.0")]

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
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                if (args.Contains("--prepare-uninstall"))
                    throw new UnauthorizedAccessException("Для удаления драйвера нужны права администратора. Запусти uninstall.exe из папки Zapret GUI от имени администратора.");
                try { using (Process elevated = Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" })) { } }
                catch (Win32Exception error) { if (error.NativeErrorCode != 1223) throw; Environment.ExitCode = 1; }
                return;
            }
            if (args.Contains("--prepare-uninstall")) { Maintenance.BeforeUninstall(); return; }
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
            try
            {
                using (StartupOverlay loading = new StartupOverlay())
                {
                    MainWindow main = new MainWindow();
                    EventHandler finishLoading = null;
                    finishLoading = delegate { main.Shown -= finishLoading; loading.Dispose(); main.BeginInvoke((MethodInvoker)delegate { if (!main.IsDisposed && main.Visible) GuideExperience.Invite(main); }); };
                    main.Shown += finishLoading;
                    Application.Run(main);
                }
            }
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
        return name.Equals("general", StringComparison.OrdinalIgnoreCase) ? "Основной альт" : Regex.Replace(name, @"^general\s*", "", RegexOptions.IgnoreCase).Trim('(', ')').Replace("ALT", "Альт ").Trim();
    }
}

internal sealed class MainWindow : SessionForm
{
    private readonly TextBox folderBox = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly GlassListBox strategies = new GlassListBox { Dock = DockStyle.Fill, IntegralHeight = false };
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
    private readonly Label selectedHeading = Theme.Caption("Выбери альт", 22, true);
    private readonly CheckBox animations = new SoftToggle { Text = "Плавные анимации", AutoSize = false, Width = 180, Height = 50, Checked = true };
    private readonly CheckBox advanced = new SoftToggle { Text = "Advanced mode\r\nРасширенные настройки", AutoSize = false, Width = 180, Height = 88 };
    private readonly Button localFiles = new SoftButton { Text = "Файлы Zapret", Width = 184, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button advancedSettings = new SoftButton { Text = "Технические настройки", Width = 184, Height = 56 };
    private readonly Panel technical = new Panel { Dock = DockStyle.Fill, Height = 164, MinimumSize = new Size(0, 164) };
    private readonly Label recommendationHint = Theme.Caption("Альт — набор настроек обхода блокировок. Если доступ не появился, попробуй другой или запусти тестирование.", 10, false);
    private readonly ToolTip hints = new ThemedToolTip { AutoPopDelay = 15000, InitialDelay = 500 };

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
        themeSwitch.CheckedChanged += delegate
        {
            try { if (Theme.Light != themeSwitch.Checked) Theme.Switch(themeSwitch.Checked); SaveSettings(); }
            catch (Exception error) { themeSwitch.Checked = Theme.Light; Error(error.Message); }
        };
        advanced.CheckedChanged += delegate { ApplyMode(); SaveSettings(); };
        strategies.DrawMode = DrawMode.OwnerDrawFixed;
        strategies.ItemHeight = 58;
        strategies.DrawItem += delegate(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Strategy item = (Strategy)strategies.Items[e.Index];
            bool best = Path.GetFileName(item.FilePath).Equals(recommended, StringComparison.OrdinalIgnoreCase);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            // GlassListBox has already painted one shared background for all rows.
            if (!Theme.SceneBackground) using (Brush background = new SolidBrush(strategies.BackColor)) e.Graphics.FillRectangle(background, e.Bounds);
            if (best || selected)
                using (System.Drawing.Drawing2D.GraphicsPath path = Theme.Round(new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 5, e.Bounds.Height - 4), Theme.Aero ? 3 : 15))
                using (Brush fill = new SolidBrush(Theme.Liquid ? Color.FromArgb(95, Theme.Selected) : best ? Theme.Selected : Theme.Raised))
                { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.FillPath(fill, path); }
            Rectangle line = new Rectangle(e.Bounds.X + 16, e.Bounds.Y + 7, e.Bounds.Width - 24, 24);
            TextRenderer.DrawText(e.Graphics, item.ToString() + (best ? "  ·  рекомендован" : ""), Font, line,
                best ? Theme.Accent : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            line.Y += 24; line.Height = 20;
            string detail = advanced.Checked ? Path.GetFileName(item.FilePath) : best ? "Лучший результат последнего теста в твоей сети" : selected ? "Выбран для следующего запуска" : "Нажми, чтобы выбрать этот вариант";
            TextRenderer.DrawText(e.Graphics, detail, Font, line, Theme.Muted, TextFormatFlags.EndEllipsis);
            if (selected) using (Pen accent = new Pen(Theme.Accent, 3)) { accent.StartCap = accent.EndCap = System.Drawing.Drawing2D.LineCap.Round; e.Graphics.DrawLine(accent, e.Bounds.X + 5, e.Bounds.Y + 12, e.Bounds.X + 5, e.Bounds.Bottom - 12); }
            if ((e.State & DrawItemState.Focus) != 0 && strategies.Focused)
                using (System.Drawing.Drawing2D.GraphicsPath focus = Theme.Round(new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 5, e.Bounds.Height - 4), Theme.Aero ? 3 : 15))
                using (Pen outline = new Pen(Theme.Accent)) e.Graphics.DrawPath(outline, focus);
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
        Action refreshTray = delegate { Icon oldIcon = tray.Icon; tray.Icon = Brand.CreateIcon(32); if (oldIcon != null) oldIcon.Dispose(); trayMenu.BackColor = Theme.Panel; trayMenu.ForeColor = Theme.Text; foreach (ToolStripItem item in trayMenu.Items) item.ForeColor = Theme.Text; trayMenu.Invalidate(); };
        Theme.Changed += refreshTray; FormClosed += delegate { Theme.Changed -= refreshTray; };
        tray.Icon = Brand.CreateIcon(32); tray.Text = "Zapret GUI"; tray.ContextMenuStrip = trayMenu; tray.Visible = true;
        tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
        startupButton.Click += delegate
        {
            bool enabled = !serviceState.Automatic;
            string root = folder, file = SelectedFile();
            RunOperation(delegate
            {
                ZapretService.EnsureNoForeignService(root);
                if (enabled)
                {
                    ZapretService.CheckFiles(root);
                    if (!ZapretService.Read(root).Exists)
                    {
                        foreach (Process other in Process.GetProcessesByName("winws"))
                            using (other) throw new IOException("Сначала останови отдельную копию Zapret, запущенную через BAT.");
                        string log = ServiceBridge.Run(root, "service_install", file, true);
                        if (!ZapretService.Read(root).Owned) throw new IOException("Служба для автозапуска не создана.\r\n" + log);
                    }
                }
                ZapretService.SetAutomatic(root, enabled);
                return enabled ? "Автозапуск службы настроен. " + (ZapretService.Read(root).Running ? "Zapret сейчас работает." : "Сейчас служба остановлена: нажми «Включить Zapret». Если запуск не удаётся, открой диагностику автозапуска.") : "Автозагрузка отключена. Текущий запуск продолжает работать.";
            });
        };
        Shown += delegate { RestoreSettings(); Poll(); timer.Start(); IssueRepair.StartAutomatic(); };
        ApplyMode();
        UpdateButtons();
    }

    private void BuildInterface()
    {
        TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 232)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        TableLayoutPanel sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 24, 12, 14), Tag = "soft-panel", Margin = new Padding(8, 12, 0, 12), RowCount = 4, ColumnCount = 1 };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 200)); sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 142)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
        FlowLayoutPanel brand = new GlassFlowPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        PictureBox logo = new PictureBox { Image = Brand.CreateImage(96), Size = new Size(80, 80), SizeMode = PictureBoxSizeMode.Zoom };
        logo.Disposed += delegate { logo.Image.Dispose(); };
        Action updateLogo = delegate { Image old = logo.Image; logo.Image = Brand.CreateImage(96); if (old != null) old.Dispose(); };
        Theme.Changed += updateLogo; logo.Disposed += delegate { Theme.Changed -= updateLogo; };
        brand.Controls.Add(logo); brand.Controls.Add(Theme.Caption("Zapret GUI", 18, true));
        sidebar.Controls.Add(brand, 0, 0);
        FlowLayoutPanel navigation = new GlassFlowPanel { FitChildrenWidth = true, AutoScroll = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Button home = new SoftButton { Text = "Подключение", Width = 184, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
        home.Click += delegate { strategies.Focus(); };
        features.Text = "Списки и обновления"; features.AutoSize = false; features.Size = new Size(184, 48); features.TextAlign = ContentAlignment.MiddleLeft;
        Button tests = new SoftButton { Text = "Тестирование", Width = 184, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
        tests.Click += delegate { if (folder.Length > 0 && !busy) OpenFeatures("Тестирование"); };
        Button creators = new SoftButton { Text = "Создатели", Width = 184, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
        creators.Click += delegate { using (CreatorsWindow dialog = new CreatorsWindow()) dialog.ShowDialog(this); };
        Button themes = new SoftButton { Text = "Темы", Width = 184, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
        themes.Click += delegate { using (ThemeWindow dialog = new ThemeWindow()) dialog.ShowDialog(this); };
        localFiles.Click += delegate { try { ListUpdates.OpenFolder(folder); } catch (Exception error) { Error(error.Message); } };
        Button guide = new SoftButton { Text = "Прочитать гайд", Width = 184, Height = 48, TextAlign = ContentAlignment.MiddleLeft };
        guide.Click += delegate { GuideExperience.Read(this); };
        navigation.Controls.AddRange(new Control[] { home, features, tests, themes, guide, localFiles, creators, advancedSettings });
        sidebar.Controls.Add(navigation, 0, 1); FlowLayoutPanel preferences = new GlassFlowPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        preferences.Controls.Add(advanced); preferences.Controls.Add(animations); sidebar.Controls.Add(preferences, 0, 2);
        FlowLayoutPanel footer = new GlassFlowPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        footer.Controls.Add(Theme.Caption("Версия 0.9.15", 9, false));
        Label help = Theme.Caption("Чтобы подобрать лучший альт, открой «Тестирование» и нажми «Проверить альты».", 9, false); help.MaximumSize = new Size(178, 0); footer.Controls.Add(help); sidebar.Controls.Add(footer, 0, 3);
        shell.Controls.Add(sidebar, 0, 0);
        TableLayoutPanel body = new ThemeCanvas { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 5, ColumnCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 104)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 218));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        FlowLayoutPanel heading = new GlassFlowPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Label profileCaption = Theme.Caption("ВЫБРАННЫЙ ПРОФИЛЬ", 9, false);
        profileCaption.Margin = new Padding(0, 0, 0, 5);
        selectedHeading.Margin = Padding.Empty;
        heading.Padding = new Padding(4, 4, 0, 0);
        heading.Controls.Add(profileCaption); heading.Controls.Add(selectedHeading);
        TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        themeSwitch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        themeSwitch.Margin = new Padding(12, 12, 0, 0);
        header.Controls.Add(heading, 0, 0); header.Controls.Add(themeSwitch, 1, 0);
        body.Controls.Add(header, 0, 0);
        Card connection = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) };
        FlowLayoutPanel connectionRows = new GlassFlowPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        status.Font = new Font("Segoe UI", 14F, FontStyle.Bold); status.MaximumSize = new Size(680, 0); status.Margin = new Padding(0, 0, 0, 12);
        connectionRows.Controls.Add(Theme.Caption("СОСТОЯНИЕ ПОДКЛЮЧЕНИЯ", 9, false));
        connectionRows.Controls.Add(status);
        FlowLayoutPanel actions = new GlassFlowPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        start.Text = "Включить Zapret"; start.MinimumSize = new Size(152, 44); stop.MinimumSize = new Size(110, 44); startupButton.MinimumSize = new Size(208, 44);
        actions.Controls.AddRange(new Control[] { start, stop, startupButton }); connectionRows.Controls.Add(actions);
        connectionRows.Controls.Add(Theme.Caption("Автозапуск включает Zapret в фоне вместе с Windows. Окно оболочки открывается вручную.", 9, false));
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
        FlowLayoutPanel tools = new GlassFlowPanel { Dock = DockStyle.Fill }; tools.Controls.AddRange(new Control[] { find, refresh, showLog });
        details.Controls.Add(tools, 0, 1); details.Controls.Add(eventsBox, 0, 2); technical.Controls.Add(details); body.Controls.Add(technical, 0, 3);
        body.Controls.Add(hostsHint, 0, 4); hostsHint.Cursor = Cursors.Hand; hostsHint.Click += delegate { OpenFeatures("Списки и обновления"); };
        shell.Controls.Add(body, 1, 0); Controls.Add(shell);
        shell.Tag = body.Tag = header.Tag = heading.Tag = "canvas"; Theme.Apply(this); shell.BackColor = body.BackColor = heading.BackColor = Theme.Background;
        Theme.Primary(start); home.Tag = "selected"; home.BackColor = Theme.Selected; home.ForeColor = Theme.Accent;
        hints.SetToolTip(advanced, "Показать порты, fake-файлы, управление службами, путь к сборке и журнал. Настройки Zapret при переключении режима не меняются.");
        hints.SetToolTip(startupButton, "Автозапуск службы Zapret при включении компьютера. Само окно GUI открывать не потребуется.");
        hints.SetToolTip(strategies, "Перед сменой альта останови Zapret. Зелёным отмечен лучший результат последнего теста.");
        hints.SetToolTip(features, "Обновление hosts и IP-списков, восстановление резервной копии, проверка версии.");
    }

    private void ApplyMode()
    {
        technical.Visible = advancedSettings.Visible = localFiles.Visible = advanced.Checked;
        localFiles.TabStop = advanced.Checked;
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
        try { animations.Checked = (bool?)XElement.Load(Path.Combine(dataFolder, "settings.xml")).Element("animations") ?? true; Motion.Enabled = animations.Checked; } catch { }
        try { themeSwitch.Checked = Theme.HasSavedChoice ? Theme.Light : (string)XElement.Load(Path.Combine(dataFolder, "settings.xml")).Element("theme") == "light"; } catch { themeSwitch.Checked = Theme.Light; }
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
        startupButton.Enabled = idle && strategies.SelectedItem != null && (!serviceState.Exists || serviceState.Owned);
        startupButton.Text = serviceState.Automatic ? "Отключить автозагрузку" : "Включить автозагрузку";
        find.Enabled = idle && !serviceState.Running;
        features.Enabled = advancedSettings.Enabled = idle && folder.Length > 0;
        browse.Enabled = refresh.Enabled = idle && (!serviceState.Running || !serviceState.Owned);
        strategies.Enabled = idle;
        ((GlassListBox)strategies).SelectionLocked = serviceState.Running && serviceState.Owned;
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
            ZapretService.CheckFiles(root);
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
            status.Tag = serviceState.Running ? "accent" : "strong"; status.ForeColor = serviceState.Running ? Theme.Accent : Theme.Text;
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
    protected override CreateParams CreateParams
    {
        get { CreateParams value = base.CreateParams; value.ExStyle |= 0x02000000; return value; }
    }
    private bool repaintPending;
    private void UpdateBrandIcon() { Icon previous = Icon; Icon = Brand.CreateIcon(32); if (previous != null) previous.Dispose(); }
    public SessionForm() { Theme.Changed += UpdateBrandIcon; DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Icon = Brand.CreateIcon(32); BackColor = Theme.Background; ForeColor = Theme.Text; }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (!IsHandleCreated || IsDisposed || repaintPending || WindowState == FormWindowState.Minimized) return;
        // Resizing changes the backdrop under every child HWND, even children
        // whose own dimensions stay fixed. Repaint after the final layout pass.
        repaintPending = true;
        BeginInvoke((MethodInvoker)delegate { repaintPending = false; if (!IsDisposed) Invalidate(true); });
    }
    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); Invalidate(true); }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Motion.Show(this); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= UpdateBrandIcon;
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

// A separate STA UI loop keeps the spinner moving while the main form is built.
// The logo and palette are captured before starting the thread; no shared theme
// state or main-window controls are accessed from the splash thread.
internal sealed class StartupOverlay : IDisposable
{
    private readonly ManualResetEvent ready = new ManualResetEvent(false);
    private readonly object sync = new object();
    private Thread thread;
    private LoadingWindow window;
    private bool closing;
    private readonly Bitmap logo;
    private readonly Color background, accent, muted;
    private readonly Rectangle screen;
    internal StartupOverlay()
    {
        logo = Brand.CreateImage(128);
        background = Theme.Background; accent = Theme.Accent; muted = Theme.Muted;
        screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        thread = new Thread(Run) { IsBackground = true, Name = "Zapret GUI startup animation" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        // A bounded wait ensures the splash is visible before main-form creation.
        ready.WaitOne(1500);
    }
    private void Run()
    {
        try
        {
            using (LoadingWindow form = new LoadingWindow(logo, background, accent, muted, screen))
            {
                lock (sync) { if (closing) return; window = form; }
                form.Shown += delegate { ready.Set(); lock (sync) { if (closing) form.Close(); } };
                Application.Run(form);
            }
        }
        catch { ready.Set(); } // Startup decoration must never block the application.
        finally { lock (sync) { window = null; } logo.Dispose(); }
    }
    public void Dispose()
    {
        lock (sync)
        {
            closing = true;
            if (window != null && window.IsHandleCreated && !window.IsDisposed)
                try { window.BeginInvoke((Action)delegate { window.Close(); }); } catch (InvalidOperationException) { }
        }
        // The background thread disposes its form, timer and image on exit.
        if (thread != null && thread.Join(500)) { ready.Dispose(); thread = null; }
    }
}

internal sealed class LoadingWindow : Form
{
    private readonly Bitmap logo;
    private readonly Color accent, muted;
    private readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 16 };
    private readonly Stopwatch watch = new Stopwatch();
    internal LoadingWindow(Bitmap image, Color background, Color color, Color secondary, Rectangle screen)
    {
        logo = image; accent = color; muted = secondary;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(264, 264); BackColor = background; DoubleBuffered = true;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + (screen.Height - Height) / 2);
        using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            const int d = 40;
            path.AddArc(0, 0, d, d, 180, 90); path.AddArc(Width - d, 0, d, d, 270, 90);
            path.AddArc(Width - d, Height - d, d, d, 0, 90); path.AddArc(0, Height - d, d, d, 90, 90);
            path.CloseFigure(); Region = new Region(path);
        }
        clock.Tick += delegate { Invalidate(new Rectangle(108, 194, 48, 48)); };
        Shown += delegate { watch.Start(); clock.Start(); };
        AccessibleName = "Запуск Zapret GUI";
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(logo, new Rectangle(68, 40, 128, 128));
        Rectangle ring = new Rectangle(116, 202, 32, 32);
        using (Pen track = new Pen(Color.FromArgb(45, muted), 3)) e.Graphics.DrawEllipse(track, ring);
        using (Pen progress = new Pen(accent, 3))
        {
            progress.StartCap = progress.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            float angle = (float)(watch.Elapsed.TotalMilliseconds * 0.32 % 360);
            e.Graphics.DrawArc(progress, ring, angle - 90, 95);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { clock.Stop(); clock.Dispose(); if (Region != null) { Region old = Region; Region = null; old.Dispose(); } }
        base.Dispose(disposing);
    }
}

// Kept outside the installer keys and application directory across reinstalls.
internal static class GuideExperience
{
    private const string Preferences = @"Software\ZapretGuiUserPreferences";
    internal static void Invite(Form owner)
    {
        try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Preferences)) if (key != null && Convert.ToString(key.GetValue("HideGuideOnStartup", 0)) == "1") return; }
        catch { /* An unavailable preference must not prevent startup. */ }
        bool read;
        using (GuideInvitation dialog = new GuideInvitation(owner)) read = dialog.ShowDialog(owner) == DialogResult.OK;
        if (read && !owner.IsDisposed) Read(owner);
    }
    internal static void SaveHidden()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Preferences))
        {
            if (key == null) throw new IOException("Не удалось открыть настройки пользователя.");
            key.SetValue("HideGuideOnStartup", 1, RegistryValueKind.DWord);
        }
    }
    internal static void Read(Form owner) { using (GuideWindow dialog = new GuideWindow()) dialog.ShowDialog(owner); }
}

internal sealed class GuideInvitation : Form
{
    private Bitmap backdrop;
    private readonly GuideCard card = new GuideCard();
    private readonly SoftToggle hide = new SoftToggle { Text = "Больше не показывать", AutoSize = true, Font = new Font("Segoe UI", 10F), Margin = new Padding(0, 8, 0, 12) };
    internal GuideInvitation(Form owner)
    {
        Text = "Короткий гайд по Zapret GUI"; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.None;
        Bounds = owner.RectangleToScreen(owner.ClientRectangle); DoubleBuffered = true;
        // Capture only our own window, in memory. Downsampling softens the backdrop.
        try
        {
            using (Bitmap capture = new Bitmap(owner.Width, owner.Height))
            {
                owner.DrawToBitmap(capture, new Rectangle(Point.Empty, capture.Size));
                ScrollSkin.PaintSnapshot(owner, capture);
                using (Bitmap small = new Bitmap(Math.Max(1, Width / 12), Math.Max(1, Height / 12)))
                {
                    Point offset = owner.PointToScreen(Point.Empty); offset.Offset(-owner.Left, -owner.Top);
                    using (Graphics g = Graphics.FromImage(small))
                    { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear; g.DrawImage(capture, new Rectangle(Point.Empty, small.Size), new Rectangle(offset, owner.ClientSize), GraphicsUnit.Pixel); }
                    backdrop = new Bitmap(Width, Height);
                    using (Graphics g = Graphics.FromImage(backdrop))
                    { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(small, new Rectangle(Point.Empty, backdrop.Size)); }
                }
            }
        }
        catch { if (backdrop != null) { backdrop.Dispose(); backdrop = null; } }
        card.Size = new Size(Math.Min(520, Math.Max(280, Width - 40)), Math.Min(400, Math.Max(260, Height - 32)));
        card.Location = new Point((Width - card.Width) / 2, (Height - card.Height) / 2);
        TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8), Margin = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        content.Controls.Add(new GuideGlyph { Size = new Size(56, 56), Anchor = AnchorStyles.None, Margin = Padding.Empty }, 0, 0);
        Label title = Theme.Caption("С чего начать?", 22, true);
        title.AutoSize = false; title.Dock = DockStyle.Fill; title.TextAlign = ContentAlignment.MiddleCenter; title.Margin = Padding.Empty;
        content.Controls.Add(title, 0, 1);
        Label description = Theme.Caption("Короткий гайд поможет выбрать альт, настроить DNS и разобраться со входом в Meta. Его всегда можно открыть из меню.", 11, false);
        description.AutoSize = false; description.Dock = DockStyle.Fill; description.TextAlign = ContentAlignment.MiddleCenter;
        description.Margin = new Padding(8, 4, 8, 8); content.Controls.Add(description, 0, 2);
        hide.Anchor = AnchorStyles.None; content.Controls.Add(hide, 0, 3);
        FlowLayoutPanel actions = new GlassFlowPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Anchor = AnchorStyles.None, Margin = Padding.Empty };
        Button read = new SoftButton { Text = "Прочитать гайд", Size = new Size(190, 44), Tag = "primary", Margin = new Padding(0, 0, 8, 0), Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        Button later = new SoftButton { Text = "Не сейчас", Size = new Size(145, 44), DialogResult = DialogResult.Cancel, Margin = Padding.Empty, Font = new Font("Segoe UI", 10F) };
        read.Click += delegate { DialogResult = DialogResult.OK; Close(); };
        actions.Controls.Add(read); actions.Controls.Add(later); content.Controls.Add(actions, 0, 4);
        card.Controls.Add(content); Controls.Add(card); AcceptButton = read; CancelButton = later; Theme.Apply(this);
        // Round the card itself so its background cannot leave square corners on the blur.
        card.Padding = Theme.Aero ? new Padding(20, 36, 20, 20) : new Padding(20);
        using (System.Drawing.Drawing2D.GraphicsPath outline = Theme.Round(new Rectangle(0, 0, card.Width - 1, card.Height - 1), Theme.Aero ? 8 : 20)) card.Region = new Region(outline);
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Motion.Show(this); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (backdrop != null) e.Graphics.DrawImageUnscaled(backdrop, Point.Empty); else e.Graphics.Clear(Theme.Background);
        using (Brush tint = new SolidBrush(Color.FromArgb(105, Theme.Background))) e.Graphics.FillRectangle(tint, ClientRectangle);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (hide.Checked)
            try { GuideExperience.SaveHidden(); }
            catch (Exception error) { MessageBox.Show(this, "Не удалось сохранить выбор. При следующем запуске приглашение может появиться снова.\n\n" + error.Message, "Zapret GUI", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    { if (disposing && backdrop != null) { backdrop.Dispose(); backdrop = null; } base.Dispose(disposing); }
}

internal sealed class GuideGlyph : Control
{
    internal GuideGlyph() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    { if (Theme.SceneBackground) GlassScene.Backdrop(e.Graphics, this, ClientRectangle); else base.OnPaintBackground(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (Pen pen = new Pen(Theme.Accent, 2.5F))
        {
            e.Graphics.DrawLines(pen, new Point[] { new Point(28, 46), new Point(9, 41), new Point(9, 10), new Point(28, 15), new Point(47, 10), new Point(47, 41), new Point(28, 46), new Point(28, 15) });
            e.Graphics.DrawLine(pen, 15, 21, 22, 23); e.Graphics.DrawLine(pen, 15, 28, 22, 30);
            e.Graphics.DrawLine(pen, 34, 23, 41, 21); e.Graphics.DrawLine(pen, 34, 30, 41, 28);
        }
    }
}

internal sealed class GuideWindow : SessionForm
{
    private readonly GlassFlowPanel sections = new GlassFlowPanel { Dock = DockStyle.Fill, AutoScroll = true, FitChildrenWidth = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
    internal GuideWindow()
    {
        Text = "Гайд • Zapret GUI"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 720); MinimumSize = new Size(520, 420);
        Size screen = Screen.FromPoint(Cursor.Position).WorkingArea.Size; Size = new Size(Math.Min(Width, screen.Width), Math.Min(Height, screen.Height));
        Add("Гайд по Zapret GUI", "Основные настройки и помощь, если сайт или вход в аккаунт не открывается.");
        Add("Важно: проверь DNS", "Если Meta Quest Developer Hub, Quest Link или сайты Meta не открываются, причиной может быть DNS. Одного запуска Zapret и обновления hosts может быть недостаточно.\n\nВ нашем случае обычный DNS возвращал «домен не существует» для auth.meta.com. После включения DNS по HTTPS в Windows 11 вход заработал. Это подтверждено для конкретного подключения и не гарантирует устранение любой ошибки входа.");
        Add("DNS по HTTPS в Windows 11", "1. Открой Параметры → Сеть и Интернет → активное подключение Ethernet или свойства сети Wi-Fi.\n2. Найди «Назначение DNS-сервера» → «Изменить». Сохрани прежние настройки для отката.\n3. Выбери «Вручную», включи IPv4.\n4. Предпочитаемый DNS: 1.1.1.1. Альтернативный: 1.0.0.1.\n5. Для обоих включи DNS по HTTPS с автоматическим шаблоном. Возврат к незашифрованному DNS, если доступен, выключи.\n\nМеняй назначение DNS-сервера, а не IP-адрес компьютера. Простая замена DNS без HTTPS может не помочь. Названия пунктов могут отличаться в зависимости от версии Windows.");
        Add("После изменения DNS", "Выполни в PowerShell или командной строке:\n\nipconfig /flushdns\n\nПолностью закрой проблемное приложение и браузер, затем открой снова. Оставь Zapret включённым с рабочей стратегией. Для отката верни сохранённые настройки DNS.");
        Add("Выбери рабочий альт", "На странице подключения выбери стратегию и нажми «Включить Zapret». Если нужный сервис не работает, открой «Тестирование» → «Проверить альты». Результат зависит от подключения: единого лучшего альта для всех нет. Перед сменой стратегии останови Zapret.");
        Add("Списки и hosts", "В «Списки и обновления» можно обновлять домены, работать с hosts и резервными копиями. Перед изменениями сохрани рабочую папку Zapret и hosts. После изменения списков останови и снова запусти Zapret.\n\nАвтопроверка ищет замену для существующих перенаправлений на публичные IPv4 и проверяет кандидатов по HTTPS. Записи 0.0.0.0 не изменяются. Локальные, служебные, IPv6 и конфликтующие записи пропускаются. За проход проверяется до 25 доменов, затем обход продолжается. Ответы 403 и 429 не запускают замену.\n\nПеред записью создаётся резервная копия. Эта функция не исправляет системный DNS и не гарантирует работу всех сайтов. Полное обновление hosts может перезаписать точечные исправления.");
        Add("Meta Quest и вход в аккаунт", "В приложении есть добавление доменов Meta / Quest в пользовательский список и ссылки на страницы скачивания Developer Hub и Quest Link. Если приложение открывается, но вход зависает, проверь DNS по инструкции выше и перезапусти приложение вместе с браузером. Доступ ко всем сервисам Meta не гарантируется.");
        Add("Гайд всегда под рукой", "Кнопка «Прочитать гайд» находится в боковом меню. Галочка «Больше не показывать» скрывает только приглашение при запуске. Выбор сохраняется для текущей учётной записи Windows и переживает обычное обновление, удаление и повторную установку. При удалении пользовательских настроек или смене учётной записи приглашение появится снова.");
        Button close = new SoftButton { Text = "Понятно", Dock = DockStyle.Bottom, Height = 48, DialogResult = DialogResult.OK };
        sections.SizeChanged += delegate { FitText(); };
        Controls.Add(sections); Controls.Add(close); CancelButton = close; Theme.Apply(this); FitText();
    }
    private void FitText()
    {
        int width = Math.Max(160, sections.ClientSize.Width - sections.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
        foreach (Control label in sections.Controls) label.MaximumSize = new Size(width, 0);
    }
    private void Add(string heading, string body)
    {
        Label title = Theme.Caption(heading, 17, true); title.Margin = new Padding(0, 12, 0, 10);
        Label text = Theme.Caption(body, 11, false); text.Margin = new Padding(0, 0, 0, 20);
        sections.Controls.Add(title); sections.Controls.Add(text);
    }
}

// The invitation owns one complete surface, with matching paint and clip bounds.
internal sealed class GuideCard : Panel
{
    internal GuideCard() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); Padding = new Padding(20); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Rectangle r = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        int radius = Theme.Aero ? 8 : 20;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(Theme.Panel);
        using (System.Drawing.Drawing2D.GraphicsPath path = Theme.Round(r, radius))
        {
            System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save();
            e.Graphics.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect);
            if (Theme.SceneBackground) GlassScene.Backdrop(e.Graphics, this, ClientRectangle);
            if (Theme.Aero) ThemeArt.AeroEdge(e.Graphics, r, radius, Theme.Border);
            else if (Theme.Liquid) GlassScene.Rim(e.Graphics, r, radius);
            else using (Pen edge = new Pen(Theme.Border)) e.Graphics.DrawPath(edge, path);
            e.Graphics.Restore(state);
        }
    }
}
