using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Theme
{
    internal static bool Light;
    internal static event Action Changed;
    private static readonly HashSet<Control> wired = new HashSet<Control>();
    internal static ThemeSpec Current;
    internal static bool HasSavedChoice;
    private static Color[] colors, targetColors;
    private static Color primaryInk, targetInk;
    private static double radius, targetRadius, effect, targetEffect;
    internal static bool Liquid { get { return Current.Finish == "glass"; } }
    internal static bool Aero { get { return Current.Finish == "aero"; } }
    internal static bool SceneBackground { get { return Liquid || Aero || Current.Wallpaper != null; } }
    internal static int Radius { get { return (int)Math.Round(radius); } }
    internal static int Revision { get; private set; }
    private static double lightness, targetLightness;
    internal static double Lightness { get { return lightness; } }
    static Theme()
    {
        Current = ThemeStore.LoadChoice(out Light, out HasSavedChoice);
        colors = (Color[])(Light ? Current.Day : Current.Night).Clone();
        primaryInk = targetInk = ThemeSpec.Ink(colors[6]);
        targetColors = colors; radius = targetRadius = Current.Radius;
        effect = targetEffect = Current.Finish == "flat" ? 0 : 1;
        lightness = targetLightness = Light ? 1 : 0;
        Motion.Changed += delegate { if (!Motion.Enabled) ThemeFade.FinishAll(); };
        Application.ApplicationExit += delegate { ThemeFade.FinishAll(); };
    }
    private static void Settle() { colors = (Color[])targetColors.Clone(); primaryInk = targetInk; radius = targetRadius; effect = targetEffect; lightness = targetLightness; }
    internal static void Switch(bool light) { Use(Current, light); }
    internal static void Use(ThemeSpec next, bool light)
    {
        // Persist before changing the visible theme. A failed save leaves the current theme intact.
        ThemeStore.SaveChoice(next.Id, light);
        bool animate = Motion.Enabled && SystemInformation.UIEffectsEnabled;
        Dictionary<Form, Bitmap> snapshots = animate ? ThemeFade.CaptureAll() : null;
        ThemeFade.FinishAll();
        HasSavedChoice = true;
        Revision++;
        Current = next; Light = light; targetColors = (Color[])(light ? next.Day : next.Night).Clone();
        targetInk = ThemeSpec.Ink(targetColors[6]);
        targetRadius = next.Radius; targetEffect = next.Finish == "flat" ? 0 : 1;
        targetLightness = light ? 1 : 0;
        Settle(); Motion.FinishTransitions();
        try { RepaintTheme(true); if (snapshots != null) ThemeFade.Play(snapshots); }
        finally { if (snapshots != null) foreach (Bitmap bitmap in snapshots.Values) bitmap.Dispose(); }
    }
    private static void RepaintTheme(bool finished)
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form.IsDisposed || (!finished && !form.Visible)) continue;
            PaintColors(form);
            if (finished) { NativeTheme(form); DarkTitle(form); }
            form.Invalidate(true);
        }
        if (finished && Changed != null) Changed();
    }
    private static void NativeTheme(Control control)
    {
        if (control.IsHandleCreated && (control is TextBox || control is ComboBox || control is ListBox))
            try { SetWindowTheme(control.Handle, (Light || Aero) ? "Explorer" : "DarkMode_Explorer", null); } catch { }
        if (control is Card) control.Padding = Aero ? new Padding(14, 36, 14, 14) : new Padding(20);
        if ((string)control.Tag == "soft-panel") { Clip(control, Aero ? 3 : 20); control.Padding = Aero ? new Padding(18, 36, 12, 14) : new Padding(18, 24, 12, 14); }
        if (control is ListBox || control is TextBox || control is ComboBox) Clip(control, Aero ? 0 : 12);
        foreach (Control child in control.Controls) NativeTheme(child);
    }
    internal static Color Background { get { return colors[0]; } }
    internal static Color Panel { get { return colors[1]; } }
    internal static Color Raised { get { return colors[2]; } }
    internal static Color Border { get { return colors[3]; } }
    internal static Color Text { get { return colors[4]; } }
    internal static Color Muted { get { return colors[5]; } }
    internal static Color Accent { get { return colors[6]; } }
    internal static Color Selected { get { return colors[7]; } }
    internal static Color Error { get { return colors[8]; } }
    internal static Color PrimaryText { get { return primaryInk; } }
    internal static Color Hover { get { return Motion.Blend(Raised, Accent, 0.17); } }
    internal static Color PrimaryHover { get { return Motion.Blend(Accent, Color.White, 0.16); } }
    internal static void Surface(Graphics g, Rectangle bounds, Color fill, int rounding)
    { ThemeArt.Surface(g, bounds, fill, Border, Accent, Current.Finish, rounding, effect); }
    private static void PaintColor(Control control)
    {
        if (control is ColorChip || control is ThemePreview) return;
        string role = control.Tag as string;
        Color back = control is Form || role == "canvas" ? Background : Panel;
        Color ink = role == "muted" ? Muted : role == "accent" ? Accent : role == "error" ? Error : Text;
        if (control is Label || (SceneBackground && control is PictureBox)) back = Color.Transparent;
        if (control is TextBox) back = Background;
        if (control is ComboBox || control is Button) back = Raised;
        Button button = control as Button;
        if (button != null)
        {
            button.FlatAppearance.BorderColor = Border;
            if (role == "primary") { back = Accent; ink = PrimaryText; }
            if (role == "selected") { back = Selected; ink = Accent; }
        }
        // Set each property only once: changing a transparent label's background
        // to an opaque color and back was invalidating its ancestors every frame.
        if (control.BackColor != back) control.BackColor = back;
        if (control.ForeColor != ink) control.ForeColor = ink;
    }
    private static void PaintColors(Control control)
    {
        PaintColor(control);
        foreach (Control child in control.Controls) PaintColors(child);
    }
    internal static Label Caption(string text, int size, bool strong)
    {
        return new Label { Text = text, Tag = strong ? "strong" : "muted", AutoSize = true, ForeColor = strong ? Text : Muted,
            Font = new Font("Segoe UI", size, strong ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 0, 0, 10), MaximumSize = new Size(670, 0) };
    }
    internal static void Apply(Control control)
    {
        if (control is ColorChip || control is ThemePreview) return;
        control.BackColor = control is Form || (string)control.Tag == "canvas" ? Background : Panel;
        control.ForeColor = (string)control.Tag == "muted" ? Muted : Text;
        if (control is Label || (SceneBackground && control is PictureBox)) control.BackColor = Color.Transparent;
        TextBox box = control as TextBox;
        if (box != null) { box.BackColor = Background; box.BorderStyle = BorderStyle.None; }
        ListBox list = control as ListBox;
        if (list != null) { list.BackColor = Panel; list.BorderStyle = BorderStyle.None; }
        Button button = control as Button;
        if (button != null)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border; button.FlatAppearance.MouseOverBackColor = Raised;
            button.BackColor = Raised; button.ForeColor = Text; button.Cursor = Cursors.Hand;
            button.Padding = new Padding(12, 5, 12, 5); button.MinimumSize = new Size(button.MinimumSize.Width, Math.Max(38, button.MinimumSize.Height));
        }
        ComboBox choice = control as ComboBox;
        if (choice != null) { choice.FlatStyle = FlatStyle.Flat; choice.BackColor = Raised; choice.ForeColor = Text; }
        CheckBox check = control as CheckBox;
        if (check != null) { check.FlatStyle = FlatStyle.Flat; check.ForeColor = Text; }
        if ((control is TextBox || control is ComboBox || control is ListBox) && wired.Add(control))
        {
            EventHandler shape = delegate { Clip(control, Aero ? 0 : 12); };
            control.SizeChanged += shape; control.HandleCreated += shape; Clip(control, Aero ? 0 : 12);
            control.HandleCreated += delegate { try { SetWindowTheme(control.Handle, (Light || Aero) ? "Explorer" : "DarkMode_Explorer", null); if (control is TextBox) SendMessage(control.Handle, 0xD3, new IntPtr(3), new IntPtr(8 | (8 << 16))); } catch { } };
            control.Disposed += delegate { wired.Remove(control); };
        }
        if (control.IsHandleCreated && (control is TextBox || control is ComboBox || control is ListBox))
            try { SetWindowTheme(control.Handle, (Light || Aero) ? "Explorer" : "DarkMode_Explorer", null); } catch { }
        if (button != null && (string)button.Tag == "primary") Primary(button);
        if (button != null && (string)button.Tag == "selected") { button.BackColor = Selected; button.ForeColor = Accent; }
        if (control is Panel && wired.Add(control))
        {
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(control, true, null);
            if ((string)control.Tag == "soft-panel")
            {
                EventHandler shape = delegate { Clip(control, Aero ? 3 : 20); };
                control.SizeChanged += shape; shape(control, EventArgs.Empty);
            }
            control.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (SceneBackground && !(control is Card) && !(control is ThemeCanvas))
                {
                    GlassScene.Backdrop(e.Graphics, control, control.ClientRectangle);
                    if ((string)control.Tag == "soft-panel") { if (Aero) ThemeArt.AeroEdge(e.Graphics, Rectangle.Inflate(control.ClientRectangle, -1, -1), Radius, Border); else GlassScene.Rim(e.Graphics, Rectangle.Inflate(control.ClientRectangle, -1, -1), 24); }
                }
            };
            control.SizeChanged += delegate { if (SceneBackground) control.Invalidate(true); };
            control.LocationChanged += delegate { if (SceneBackground) control.Invalidate(true); };
            control.Disposed += delegate { wired.Remove(control); };
        }
        if (control is Card) control.Padding = Aero ? new Padding(14, 36, 14, 14) : new Padding(20);
        if ((string)control.Tag == "soft-panel" && Aero) control.Padding = new Padding(18, 36, 12, 14);
        PaintColor(control);
        foreach (Control child in control.Controls) Apply(child);
    }
    internal static void Primary(Button button)
    {
        button.Tag = "primary"; button.BackColor = Accent; button.ForeColor = PrimaryText;
        button.FlatAppearance.BorderColor = Accent;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(148, 237, 196);
        if (button.Font.Size != 11F || !button.Font.Bold) button.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
    }
    internal static void DarkTitle(Form form)
    {
        try
        {
            int enabled = Light ? 0 : 1; DwmSetWindowAttribute(form.Handle, 20, ref enabled, 4);
            int rounded = 2; DwmSetWindowAttribute(form.Handle, 33, ref rounded, 4);
            int caption = Aero ? ColorTranslator.ToWin32(Light ? Color.FromArgb(175, 198, 218) : Color.FromArgb(48, 77, 101)) : -1;
            int ink = Aero ? ColorTranslator.ToWin32(Text) : -1, border = Aero ? ColorTranslator.ToWin32(Border) : -1;
            DwmSetWindowAttribute(form.Handle, 34, ref border, 4);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
            DwmSetWindowAttribute(form.Handle, 36, ref ink, 4);
        }
        catch { }
    }
    internal static GraphicsPath Round(Rectangle bounds, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    internal static void Clip(Control control, int radius)
    {
        if (control.Width < 2 || control.Height < 2) return;
        Region previous = control.Region;
        if (radius == 0) { control.Region = null; if (previous != null) previous.Dispose(); return; }
        using (GraphicsPath path = Round(new Rectangle(0, 0, control.Width, control.Height), radius)) control.Region = new Region(path);
        if (previous != null) previous.Dispose();
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string app, string id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

internal sealed class Card : Panel
{
    internal Card() { SetStyle(ControlStyles.ResizeRedraw, true); DoubleBuffered = true; BackColor = Theme.Panel; Padding = new Padding(20); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Theme.SceneBackground)
        {
            GlassScene.Backdrop(e.Graphics, Parent ?? this, new Rectangle(Left, Top, Width, Height), new Point(-Left, -Top));
            if (Width > 20 && Height > 20)
            {
                Rectangle r = new Rectangle(2, 2, Width - 5, Height - 5);
                using (GraphicsPath clip = Theme.Round(r, Theme.Aero ? 3 : Theme.Radius))
                {
                    GraphicsState state = e.Graphics.Save(); e.Graphics.SetClip(clip, CombineMode.Intersect);
                    GlassScene.Backdrop(e.Graphics, this, ClientRectangle); e.Graphics.Restore(state);
                }
                if (Theme.Aero) ThemeArt.AeroEdge(e.Graphics, r, Theme.Radius, Theme.Border);
                else GlassScene.Rim(e.Graphics, r, Theme.Radius);
            }
            return;
        }
        e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
        if (Width < 25 || Height < 25) return;
        Theme.Surface(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Theme.Panel, Theme.Radius);
    }
}

// Navigation owns pages directly: hidden advanced pages are not reachable by Tab.
internal sealed class SettingsPages : Panel
{
    private readonly FlowLayoutPanel navigation = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 170, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8), AutoScroll = true };
    private readonly Panel content = new Panel { Dock = DockStyle.Fill };
    private readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
    private readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
    private string selected;
    internal SettingsPages() { Dock = DockStyle.Fill; Controls.Add(content); Controls.Add(navigation); }
    internal void AddPage(string name, Control page, bool accessible)
    {
        page.Dock = DockStyle.Fill; page.Visible = false; content.Controls.Add(page);
        pages.Add(name, page);
        Button button = new SoftButton { Text = name, Width = 148, Height = 58, TextAlign = ContentAlignment.MiddleLeft, Visible = accessible, TabStop = accessible };
        button.Click += delegate { SelectPage(name); };
        buttons.Add(name, button); navigation.Controls.Add(button);
        if (accessible && selected == null) SelectPage(name);
    }
    internal void SelectPage(string name)
    {
        if (!pages.ContainsKey(name) || !buttons[name].TabStop) return;
        selected = name;
        foreach (KeyValuePair<string, Control> pair in pages) pair.Value.Visible = pair.Key == name;
        pages[name].BringToFront();
        foreach (KeyValuePair<string, Button> pair in buttons)
        { pair.Value.Tag = pair.Key == name ? "selected" : null; pair.Value.BackColor = pair.Key == name ? Theme.Selected : Theme.Panel; pair.Value.ForeColor = pair.Key == name ? Theme.Accent : Theme.Muted; }
    }
    internal void Restyle() { Theme.Apply(this); if (selected != null) SelectPage(selected); }
}

internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
    public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
    public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
    public override Color MenuItemSelected { get { return Theme.Selected; } }
    public override Color MenuItemBorder { get { return Theme.Border; } }
    public override Color MenuBorder { get { return Theme.Border; } }
    public override Color SeparatorDark { get { return Theme.Border; } }
    public override Color SeparatorLight { get { return Theme.Border; } }
}

// Preserve the standard Button click, keyboard and accessibility behavior;
// paint a rounded surface with explicit readable disabled/focus states.
internal sealed class SoftButton : Button
{
    private bool hovered, pressed;
    private PointF lightSpot = new PointF(0.35F, 0.2F);
    private readonly SmoothValue hover;
    internal SoftButton()
    {
        hover = new SmoothValue(this);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; hover.Set(1); base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; hover.Set(0); base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Theme.Liquid && Motion.Enabled) { lightSpot = new PointF(e.X / (float)Math.Max(1, Width), e.Y / (float)Math.Max(1, Height)); Invalidate(); }
    }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Color parent = Parent == null ? Theme.Panel : Parent.BackColor;
        if (Theme.SceneBackground) GlassScene.Backdrop(e.Graphics, this, ClientRectangle); else e.Graphics.Clear(parent); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        bool primary = (string)Tag == "primary";
        Color fill = !Enabled ? Theme.Panel : pressed ? Theme.Selected : Motion.Blend(BackColor, primary ? Theme.PrimaryHover : Theme.Hover, hover.Value);
        using (GraphicsPath path = Theme.Round(new Rectangle(1, 1, Math.Max(1, Width-3), Math.Max(1, Height-3)), (Theme.Aero ? 3 : Math.Min(Theme.Radius, 22))))
        using (Brush brush = new SolidBrush(fill))
        using (Pen border = new Pen(Focused && ShowFocusCues ? Theme.Accent : primary && Enabled ? Theme.Accent : Theme.Border))
        {
            if (Theme.Liquid && primary && Enabled) using (Brush tint = new SolidBrush(Color.FromArgb(225, fill))) e.Graphics.FillPath(tint, path);
            Theme.Surface(e.Graphics, new Rectangle(1, 1, Math.Max(1, Width-3), Math.Max(1, Height-3)), fill, Math.Min(Theme.Radius, 22));
            if ((!Theme.Liquid && !Theme.Aero) || (Focused && ShowFocusCues)) e.Graphics.DrawPath(border, path);
        }
        if (Theme.Liquid && Enabled && hovered)
            GlassScene.Glint(e.Graphics, new Rectangle(2, 2, Width - 5, Height - 5), Math.Min(Theme.Radius, 22), Motion.Enabled ? lightSpot : new PointF(0.35F, 0.2F));
        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
        flags |= TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(12, 5, Math.Max(1,Width-24), Math.Max(1,Height-10)), Enabled ? ForeColor : Theme.Muted, flags);
    }
}

internal sealed class SoftToggle : CheckBox
{
    private readonly SmoothValue slide;
    internal SoftToggle() { slide = new SmoothValue(this); SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font);
        return new Size(text.Width + 44, Math.Max(28, text.Height + 8));
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); if (slide != null) slide.Set(Checked ? 1 : 0); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Theme.SceneBackground) GlassScene.Backdrop(e.Graphics, this, ClientRectangle); else e.Graphics.Clear(Parent == null ? Theme.Panel : Parent.BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int y = (Height - 18) / 2;
        using (GraphicsPath track = Theme.Round(new Rectangle(1, y, 30, 18), 9))
        using (Brush fill = new SolidBrush(Motion.Blend(Theme.Border, Theme.Accent, slide.Value))) e.Graphics.FillPath(fill, track);
        using (Brush knob = new SolidBrush(Checked ? Theme.Background : Theme.Muted)) e.Graphics.FillEllipse(knob, (float)(4 + 12 * slide.Value), y + 3, 12, 12);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(40, 0, Math.Max(1,Width-42), Height), Enabled ? ForeColor : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        if (Focused && ShowFocusCues) using (Pen pen = new Pen(Theme.Accent)) e.Graphics.DrawLine(pen, 40, Height-2, Width-3, Height-2);
    }
}

internal sealed class ThemeSpec
{
    internal string Id, Name, Finish;
    internal bool BuiltIn;
    internal byte[] Wallpaper, Logo;
    internal bool LiveNight;
    internal byte[] WallpaperFor(bool light) { return LiveNight && !light ? BuiltinNightArtwork.Picture : Wallpaper; }
    internal string ImageFit = "cover";
    internal int ImageOpacity = 75;
    internal int Radius;
    internal Color[] Day, Night;
    internal static readonly string[] Keys = { "background", "panel", "raised", "border", "text", "muted", "accent", "selected", "error" };
    internal static readonly string[] Labels = { "Фон окна", "Карточки", "Кнопки", "Границы", "Основной текст", "Подписи", "Акцент", "Выделение", "Ошибки" };
    public override string ToString() { return Name + (BuiltIn ? "" : " · своя тема"); }
    internal ThemeSpec Copy()
    { return new ThemeSpec { Id = Id, Name = Name, Finish = Finish, BuiltIn = BuiltIn, Radius = Radius, Wallpaper = Wallpaper, Logo = Logo, LiveNight = LiveNight, ImageFit = ImageFit, ImageOpacity = ImageOpacity, Day = (Color[])Day.Clone(), Night = (Color[])Night.Clone() }; }
    internal static Color[] Colors(string values)
    { return values.Split(' ').Select(ParseColor).ToArray(); }
    internal static Color ParseColor(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$")) throw new InvalidDataException("Цвет должен быть в формате #RRGGBB.");
        return Color.FromArgb(255, ColorTranslator.FromHtml(value));
    }
    internal static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
    private static double Channel(byte value) { double c = value / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
    private static double Luma(Color c) { return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B); }
    internal static double Contrast(Color a, Color b) { double x = Luma(a), y = Luma(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
    internal static Color Ink(Color c) { return Contrast(c, Color.White) >= Contrast(c, Color.FromArgb(12, 22, 30)) ? Color.White : Color.FromArgb(12, 22, 30); }
    internal string Readability()
    {
        foreach (Color[] p in new[] { Day, Night })
            if (Contrast(p[4], p[0]) < 4.5 || Contrast(p[4], p[1]) < 4.5 || Contrast(p[4], p[2]) < 4.5 || Contrast(p[5], p[1]) < 3)
                return "Некоторый текст может плохо читаться. Увеличь разницу между цветами текста и фона.";
        return "Контраст основных цветов достаточный.";
    }
}

internal static class ThemeStore
{
    internal static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretSimpleGui", "themes"); } }
    private static string ChoicePath { get { return Path.Combine(Folder, "current.xml"); } }
    internal static string LoadWarning = "";
    private static ThemeSpec Built(string id, string name, string finish, int radius, string day, string night)
    { return new ThemeSpec { Id = id, Name = name, Finish = finish, Radius = radius, Day = ThemeSpec.Colors(day), Night = ThemeSpec.Colors(night), BuiltIn = true }; }
    internal static List<ThemeSpec> All()
    {
        List<ThemeSpec> result = new List<ThemeSpec>();
        result.Add(Built("classic", "Classic Mint", "flat", 20,
            "#F4F8F7 #FFFFFF #E9F1ED #C4D6CC #182E27 #4F695D #167350 #D7EFE2 #A8282A",
            "#0F1A21 #172C36 #203842 #314B55 #F1F9F6 #ABC0C7 #72DFB0 #234B44 #FFA4A0"));
        result.Add(Built("glass", "Liquid Glass", "glass", 32,
            "#DEE8F3 #F5F8FC #E4EEF8 #B5C6DD #17263D #445871 #145FD0 #D9E8FC #A62C48",
            "#101725 #233044 #2A3B53 #7D93B2 #F7FAFF #C5D3E8 #B7CEFF #324B6A #FFB2C2"));
        ThemeSpec aero = Built("aero", "Frutiger Aero", "aero", 8,
            "#DDEAF6 #F4F8FC #D8E7F3 #849EB5 #17324A #36566F #246F9D #D0E8F7 #B33443",
            "#183047 #233E56 #345772 #7799B5 #F5FAFF #C5D8E8 #A4DCF6 #345F7D #FFAFAD");
        aero.Wallpaper = BuiltinArtwork.AeroWallpaper; aero.ImageOpacity = 100; aero.LiveNight = true;
        result.Add(aero);
        LoadWarning = "";
        try { if (Directory.Exists(Folder))
            foreach (string path in Directory.GetFiles(Folder, "*.zttheme").OrderBy(x => x).Take(200))
                try { ThemeSpec spec = Read(path); spec.Id = "user-" + Path.GetFileNameWithoutExtension(path); CustomPath(spec.Id); result.Add(spec); }
                catch (Exception e) { LoadWarning = "Некоторые темы не загружены: " + Path.GetFileName(path) + ". " + e.Message; }
        } catch (Exception e) { LoadWarning = "Не удалось прочитать папку тем: " + e.Message; }
        return result;
    }
    private static XElement ReadXml(string path)
    {
        if (new FileInfo(path).Length > 6 * 1024 * 1024) throw new InvalidDataException("Файл темы должен быть меньше 6 МБ.");
        XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 6 * 1024 * 1024 };
        using (XmlReader reader = XmlReader.Create(path, settings)) return XElement.Load(reader);
    }
    internal static ThemeSpec Read(string path)
    {
        XElement root = ReadXml(path);
        if (root.Name != "zapret-theme" || ((string)root.Attribute("version") != "1" && (string)root.Attribute("version") != "2")) throw new InvalidDataException("Неподдерживаемый формат темы.");
        string name = ((string)root.Attribute("name") ?? "").Trim();
        string finish = (string)root.Attribute("finish"); int radius;
        if (name.Length == 0 || name.Length > 48 || name.Any(char.IsControl)) throw new InvalidDataException("Название темы: от 1 до 48 символов.");
        if (finish != "flat" && finish != "glass" && finish != "aero") throw new InvalidDataException("Неизвестный стиль темы.");
        if (!int.TryParse((string)root.Attribute("radius"), out radius) || radius < 8 || radius > 32) throw new InvalidDataException("Скругление: от 8 до 32.");
        Func<string, Color[]> palette = delegate(string mode)
        {
            XElement node = root.Element(mode); if (node == null) throw new InvalidDataException("Нет палитры " + mode);
            return ThemeSpec.Keys.Select(key => ThemeSpec.ParseColor((string)node.Element(key))).ToArray();
        };
        ThemeSpec spec = new ThemeSpec { Id = "", Name = name, Finish = finish, Radius = radius, Day = palette("light"), Night = palette("dark") };
        spec.ImageFit = (string)root.Attribute("image-fit") ?? "cover";
        if (spec.ImageFit != "cover" && spec.ImageFit != "contain" && spec.ImageFit != "stretch") throw new InvalidDataException("Неизвестный режим фона.");
        spec.ImageOpacity = (int?)root.Attribute("image-opacity") ?? 75;
        if (spec.ImageOpacity < 0 || spec.ImageOpacity > 100) throw new InvalidDataException("Яркость фона: от 0 до 100.");
        string live = (string)root.Attribute("night-live");
        if (live != null && live != "aero-office") throw new InvalidDataException("Неизвестный видеофон темы.");
        spec.LiveNight = live == "aero-office";
        spec.Wallpaper = ThemeArtwork.Read(root.Element("wallpaper"), false);
        spec.Logo = ThemeArtwork.Read(root.Element("logo"), true);
        return spec;
    }
    private static void Atomic(string path, XElement xml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { xml.Save(temp); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static void Export(ThemeSpec spec, string path)
    {
        XElement root = new XElement("zapret-theme", new XAttribute("version", spec.Wallpaper != null || spec.Logo != null ? "2" : "1"), new XAttribute("name", spec.Name), new XAttribute("finish", spec.Finish), new XAttribute("radius", spec.Radius));
        foreach (string mode in new[] { "light", "dark" })
        {
            Color[] colors = mode == "light" ? spec.Day : spec.Night;
            XElement node = new XElement(mode);
            for (int i = 0; i < ThemeSpec.Keys.Length; i++) node.Add(new XElement(ThemeSpec.Keys[i], ThemeSpec.Hex(colors[i])));
            root.Add(node);
        }
        root.Add(new XAttribute("image-fit", spec.ImageFit), new XAttribute("image-opacity", spec.ImageOpacity));
        if (spec.LiveNight) root.Add(new XAttribute("night-live", "aero-office"));
        if (spec.Wallpaper != null) root.Add(new XElement("wallpaper", Convert.ToBase64String(spec.Wallpaper)));
        if (spec.Logo != null) root.Add(new XElement("logo", Convert.ToBase64String(spec.Logo)));
        Atomic(path, root);
    }
    private static string CustomPath(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id ?? "", "^user-[A-Za-z0-9_-]{1,80}$")) throw new InvalidDataException("Некорректный идентификатор темы.");
        return Path.Combine(Folder, id.Substring(5) + ".zttheme");
    }
    internal static void Save(ThemeSpec spec)
    {
        if (spec.BuiltIn || string.IsNullOrEmpty(spec.Id)) { spec.BuiltIn = false; spec.Id = "user-" + Guid.NewGuid().ToString("N"); }
        Export(spec, CustomPath(spec.Id));
    }
    internal static void Delete(ThemeSpec spec) { if (spec.BuiltIn) throw new InvalidOperationException("Встроенную тему удалить нельзя."); File.Delete(CustomPath(spec.Id)); }
    internal static void SaveChoice(string id, bool light)
    { Atomic(ChoicePath, new XElement("choice", new XAttribute("id", id), new XAttribute("light", light))); }
    internal static ThemeSpec LoadChoice(out bool light, out bool saved)
    {
        List<ThemeSpec> all = All(); light = false; saved = false;
        try
        {
            if (!File.Exists(ChoicePath)) return all[0];
            XElement root = ReadXml(ChoicePath); light = (bool?)root.Attribute("light") ?? false; saved = true;
            return all.FirstOrDefault(x => x.Id == (string)root.Attribute("id")) ?? all[0];
        }
        catch { return all[0]; }
    }
}

internal static class ThemeArt
{
    internal static void AeroEdge(Graphics g, Rectangle r, int radius, Color border, bool? lightMode = null)
    {
        bool light = lightMode ?? Theme.Light;
        if (r.Width < 12 || r.Height < 12) return;
        GraphicsState saved = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool panel = r.Height > 90;
            int side = panel ? 7 : 2, top = panel ? 29 : 2;
            Rectangle inner = Rectangle.FromLTRB(r.Left + side, r.Top + top, r.Right - side, r.Bottom - side);
            using (GraphicsPath outer = Theme.Round(r, panel ? 5 : 2))
            using (GraphicsPath hole = new GraphicsPath())
            using (Region ring = new Region(outer))
            {
                hole.AddRectangle(inner); ring.Exclude(hole);
                g.SetClip(ring, CombineMode.Intersect);
                using (Brush sides = new SolidBrush((light ? Color.FromArgb(181, 208, 225) : Color.FromArgb(76, 112, 138)))) g.FillRectangle(sides, r);
                Rectangle title = new Rectangle(r.Left, r.Top, r.Width, top);
                using (LinearGradientBrush blue = new LinearGradientBrush(title,
                    (light ? Color.FromArgb(151, 177, 201) : Color.FromArgb(57, 87, 112)), (light ? Color.FromArgb(193, 214, 231) : Color.FromArgb(99, 136, 160)), 90F)) g.FillRectangle(blue, title);
                using (Pen dark = new Pen((light ? Color.FromArgb(104, 132, 155) : Color.FromArgb(40, 65, 85)))) g.DrawPath(dark, outer);
                using (Pen shine = new Pen((light ? Color.FromArgb(232, 244, 251) : Color.FromArgb(133, 164, 185))))
                    g.DrawRectangle(shine, r.Left + 1, r.Top + 1, r.Width - 2, r.Height - 2);
            }
            g.Restore(saved); saved = g.Save();
            using (Pen inset = new Pen((light ? Color.FromArgb(239, 248, 253) : Color.FromArgb(115, 148, 170))))
                g.DrawRectangle(inset, inner.Left - 1, inner.Top - 1, inner.Width + 1, inner.Height + 1);
            // Painted ornaments only: no controls, hit targets or event handlers.
            if (panel && r.Width >= 100)
            {
                int y = r.Top + 2;
                Rectangle close = new Rectangle(r.Right - 49, y, 42, 21);
                Rectangle minimize = new Rectangle(close.Left - 30, y, 28, 21);
                VistaCaptionOrnament(g, minimize, false, light);
                VistaCaptionOrnament(g, close, true, light);
            }
        }
        finally { g.Restore(saved); }
    }
    private static void VistaCaptionOrnament(Graphics g, Rectangle r, bool close, bool light)
    {
        using (GraphicsPath shape = Theme.Round(r, 2))
        using (LinearGradientBrush fill = new LinearGradientBrush(r,
            close ? Color.FromArgb(228, 159, 143) : (light ? Color.FromArgb(207, 225, 240) : Color.FromArgb(127, 157, 180)),
            close ? Color.FromArgb(161, 56, 40) : (light ? Color.FromArgb(135, 164, 188) : Color.FromArgb(70, 103, 129)), 90F))
        {
            g.FillPath(fill, shape);
            GraphicsState state = g.Save();
            try
            {
                g.SetClip(shape, CombineMode.Intersect);
                using (Brush gloss = new SolidBrush(Color.FromArgb(48, Color.White)))
                    g.FillRectangle(gloss, r.Left, r.Top, r.Width, r.Height / 2);
            }
            finally { g.Restore(state); }
            using (Pen edge = new Pen(close ? Color.FromArgb(112, 62, 53) : (light ? Color.FromArgb(100, 127, 149) : Color.FromArgb(50, 78, 99)))) g.DrawPath(edge, shape);
            using (Pen highlightPen = new Pen((light ? Color.FromArgb(220, 241, 251) : Color.FromArgb(161, 185, 201))))
                g.DrawLine(highlightPen, r.Left + 2, r.Top + 1, r.Right - 2, r.Top + 1);
        }
        int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
        using (Pen outline = new Pen(Color.FromArgb(75, 82, 88), 3F))
        using (Pen ink = new Pen(Color.White, 1.6F))
        {
            if (close)
            {
                g.DrawLine(outline, cx - 4, cy - 4, cx + 4, cy + 4);
                g.DrawLine(outline, cx + 4, cy - 4, cx - 4, cy + 4);
                g.DrawLine(ink, cx - 4, cy - 4, cx + 4, cy + 4);
                g.DrawLine(ink, cx + 4, cy - 4, cx - 4, cy + 4);
            }
            else
            {
                g.DrawLine(outline, cx - 4, cy + 4, cx + 3, cy + 4);
                g.DrawLine(ink, cx - 4, cy + 4, cx + 3, cy + 4);
            }
        }
    }
    internal static void Surface(Graphics g, Rectangle r, Color fill, Color border, Color accent, string finish, int radius, double strength, bool? lightMode = null)
    {
        if (r.Width < 2 || r.Height < 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (finish == "glass") { GlassScene.Surface(g, r, fill, radius, strength); return; }
        if (finish == "aero")
        {
            using (GraphicsPath path = Theme.Round(r, 3))
            using (LinearGradientBrush wash = new LinearGradientBrush(r, Motion.Blend(fill, Color.White, .20), fill, 90F)) g.FillPath(wash, path);
            AeroEdge(g, r, Math.Min(radius, 10), border, lightMode); return;
        }
        using (GraphicsPath path = Theme.Round(r, radius))
        {
            using (Brush brush = new SolidBrush(fill)) g.FillPath(brush, path);
            if (strength > 0.001)
            {
                GraphicsState state = g.Save(); g.SetClip(path, CombineMode.Intersect);
                try
                {
                    using (LinearGradientBrush glass = new LinearGradientBrush(r, Color.FromArgb((int)(45 * strength), Color.White), Color.FromArgb(0, Color.White), 90F)) g.FillRectangle(glass, r);
                    Rectangle upper = new Rectangle(r.X, r.Y, r.Width, Math.Max(2, r.Height / (finish == "aero" ? 2 : 3)));
                    using (LinearGradientBrush sheen = new LinearGradientBrush(upper, Color.FromArgb((int)((finish == "aero" ? 60 : 30) * strength), Color.White), Color.FromArgb(0, Color.White), 90F)) g.FillRectangle(sheen, upper);

                }
                finally { g.Restore(state); }
            }
            using (Pen line = new Pen(border)) g.DrawPath(line, path);
            if (strength > 0.001 && r.Width > 8 && r.Height > 8)
                using (GraphicsPath rim = Theme.Round(Rectangle.Inflate(r, -2, -2), Math.Max(2, radius - 2)))
                using (Pen glow = new Pen(Color.FromArgb((int)(55 * strength), Color.White))) g.DrawPath(glow, rim);
        }
    }
}

internal sealed class ThemeCanvas : TableLayoutPanel
{
    internal ThemeCanvas() { DoubleBuffered = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Theme.SceneBackground) { GlassScene.Backdrop(e.Graphics, this, ClientRectangle); return; }
        base.OnPaintBackground(e);
        if (Theme.Current.Finish == "flat" || Width < 2 || Height < 2) return;
        using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, Theme.Background, Motion.Blend(Theme.Background, Theme.Accent, 0.12), 35F)) e.Graphics.FillRectangle(brush, ClientRectangle);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (Pen line = new Pen(Color.FromArgb(30, Theme.Accent), 2))
            for (int i = 0; i < 5; i++) e.Graphics.DrawEllipse(line, Width - 180 - i * 43, 20 + i * 10, 100 + i * 32, 100 + i * 32);
    }
}

internal sealed class ThemeWindow : SessionForm
{
    internal ThemeWindow()
    {
        Text = "Темы — Zapret GUI"; Font = new Font("Segoe UI", 10F); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(790, 690); MinimumSize = new Size(730, 620); StartPosition = FormStartPosition.CenterParent;
        ThemeCanvas canvas = new ThemeCanvas { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 1 };
        canvas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        canvas.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Card settings = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };
        settings.Controls.Add(new ThemesPage()); canvas.Controls.Add(settings, 0, 0);
        Controls.Add(canvas); Theme.Apply(this);
    }
}

internal sealed class ThemesPage : FlowLayoutPanel
{
    private readonly ComboBox list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 570 };
    private readonly ThemePreview preview = new ThemePreview { Width = 570, Height = 220 };
    private readonly Label hint = Theme.Caption("", 10, false);
    private readonly Button edit, remove;
    internal ThemesPage()
    {
        Dock = DockStyle.Fill; FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true; Padding = new Padding(22);
        Controls.Add(Theme.Caption("Темы", 23, true));
        Controls.Add(Theme.Caption("Выбери оформление или создай свою тему. Солнце и луна переключают светлый и тёмный варианты выбранной темы.", 10, false));
        Controls.Add(list); Controls.Add(preview);
        FlowLayoutPanel actions = new FlowLayoutPanel { Width = 590, Height = 54, WrapContents = false };
        AddButton(actions, "Применить", delegate { ThemeSpec spec = Selected; if (spec != null) { Theme.Use(spec, Theme.Light); hint.Text = "Применена тема: " + spec.Name; } });
        AddButton(actions, "Создать свою", delegate { Edit(true); });
        edit = AddButton(actions, "Изменить", delegate { Edit(false); }); Controls.Add(actions);
        FlowLayoutPanel files = new FlowLayoutPanel { Width = 590, Height = 54, WrapContents = false };
        AddButton(files, "Импорт", Import);
        AddButton(files, "Экспорт", delegate
        {
            if (Selected == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Тема Zapret GUI|*.zttheme", FileName = "my-theme.zttheme", DefaultExt = "zttheme" })
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK) { ThemeStore.Export(Selected, dialog.FileName); hint.Text = "Тема экспортирована."; }
        });
        remove = AddButton(files, "Удалить", delegate
        {
            ThemeSpec spec = Selected; if (spec == null || spec.BuiltIn) return;
            if (MessageBox.Show(FindForm(), "Удалить тему «" + spec.Name + "»?", "Темы", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (Theme.Current.Id == spec.Id) Theme.Use(ThemeStore.All()[0], Theme.Light);
            ThemeStore.Delete(spec); Reload(Theme.Current.Id);
        }); Controls.Add(files);
        hint.MaximumSize = new Size(565, 0); Controls.Add(hint);
        Controls.Add(Theme.Caption("Liquid Glass — просвечивающий фон, мягкие линзы и световые края. Frutiger Aero — небо, зелень и глянцевые поверхности.", 10, false));
        list.SelectedIndexChanged += delegate { ShowPreview(); }; Reload(Theme.Current.Id);
        Action changed = delegate { ShowPreview(); }; Theme.Changed += changed; Disposed += delegate { Theme.Changed -= changed; };
        SizeChanged += delegate { FitContent(); }; FitContent();
    }
    private void FitContent()
    {
        int available = Math.Max(240, ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (Control child in Controls)
        {
            Label label = child as Label;
            if (label != null) label.MaximumSize = new Size(available, 0);
            else child.Width = Math.Min(590, available);
        }
    }
    private ThemeSpec Selected { get { return list.SelectedItem as ThemeSpec; } }
    private Button AddButton(Control panel, string text, Action action)
    {
        Button button = new SoftButton { Text = text, AutoSize = true, MinimumSize = new Size(116, 42) };
        button.Click += delegate { try { action(); } catch (Exception e) { MessageBox.Show(FindForm(), e.Message, "Темы", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
        panel.Controls.Add(button); return button;
    }
    private void Reload(string id)
    {
        list.Items.Clear(); foreach (ThemeSpec spec in ThemeStore.All()) list.Items.Add(spec);
        int index = 0; for (int i = 0; i < list.Items.Count; i++) if (((ThemeSpec)list.Items[i]).Id == id) index = i;
        list.SelectedIndex = index; hint.Text = ThemeStore.LoadWarning;
    }
    private void ShowPreview()
    {
        preview.Spec = Selected; preview.LightMode = Theme.Light; preview.Invalidate();
        if (edit != null) edit.Enabled = Selected != null && !Selected.BuiltIn;
        if (remove != null) remove.Enabled = Selected != null && !Selected.BuiltIn;
    }
    private void Edit(bool copy)
    {
        if (Selected == null) return;
        using (ThemeEditor editor = new ThemeEditor(Selected, copy))
            if (editor.ShowDialog(FindForm()) == DialogResult.OK)
            { Reload(editor.Result.Id); hint.Text = "Тема сохранена. Нажми «Применить», чтобы включить её."; }
    }
    private void Import()
    {
        using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Тема Zapret GUI|*.zttheme", CheckFileExists = true })
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            {
                ThemeSpec spec = ThemeStore.Read(dialog.FileName); ThemeStore.Save(spec); Reload(spec.Id);
                hint.Text = "Тема импортирована. Нажми «Применить».";
            }
    }
}

internal sealed class ColorChip : Button
{
    internal Color Value;
    internal ColorChip() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 9))
        using (Brush fill = new SolidBrush(Value))
        using (Pen border = new Pen(Focused ? Theme.Accent : Theme.Border, Focused ? 2 : 1))
        { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
        TextRenderer.DrawText(e.Graphics, ThemeSpec.Hex(Value), Font, ClientRectangle, ThemeSpec.Ink(Value), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class ThemePreview : Control
{
    internal ThemeSpec Spec;
    internal bool LightMode;
    private byte[] wallpaperData, logoData;
    private Bitmap wallpaper, logo;
    internal ThemePreview() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Spec == null) { e.Graphics.Clear(Theme.Panel); return; }
        Color[] p = LightMode ? Spec.Day : Spec.Night; e.Graphics.Clear(p[0]);
        if (Spec.Finish == "glass") using (Bitmap scene = GlassScene.CreateScene(Math.Max(1, Width), Math.Max(1, Height), p[0], p[6])) e.Graphics.DrawImageUnscaled(scene, 0, 0);
        if (wallpaperData != Spec.WallpaperFor(LightMode)) { if (wallpaper != null) wallpaper.Dispose(); wallpaperData = Spec.WallpaperFor(LightMode); wallpaper = wallpaperData == null ? null : ThemeArtwork.Decode(wallpaperData); }
        if (logoData != Spec.Logo) { if (logo != null) logo.Dispose(); logoData = Spec.Logo; logo = logoData == null ? null : ThemeArtwork.Decode(logoData); }
        if (wallpaper != null) ThemeArtwork.DrawBackground(e.Graphics, ClientRectangle, Spec, wallpaper);
        int w = Width; if (w < 200 || Height < 150) return;
        ThemeArt.Surface(e.Graphics, new Rectangle(10, 10, 98, Height - 21), p[1], p[3], p[6], Spec.Finish, Spec.Radius, Spec.Finish == "flat" ? 0 : 1, LightMode);
        ThemeArt.Surface(e.Graphics, new Rectangle(122, 10, w - 134, Height - 21), p[1], p[3], p[6], Spec.Finish, Spec.Radius, Spec.Finish == "flat" ? 0 : 1, LightMode);
        int contentOffset = Spec.Finish == "aero" ? 22 : 0;
        using (Font bold = new Font("Segoe UI", 13, FontStyle.Bold))
        {
            if (Spec.Logo == null) TextRenderer.DrawText(e.Graphics, "ZT", bold, new Point(37, 27 + contentOffset), p[6]);
            else e.Graphics.DrawImage(logo, new Rectangle(33, 22 + contentOffset, 42, 42));
            TextRenderer.DrawText(e.Graphics, "Zapret работает", bold, new Point(140, 32 + contentOffset), p[4]);
        }
        TextRenderer.DrawText(e.Graphics, "Выбранный профиль · Альт 11", Font, new Point(141, 70 + contentOffset), p[5]);
        if (Spec.Finish == "glass")
            using (GraphicsPath buttonPath = Theme.Round(new Rectangle(140, 111 + contentOffset, 130, 40), 20))
            using (Brush tint = new SolidBrush(p[6])) e.Graphics.FillPath(tint, buttonPath);
        ThemeArt.Surface(e.Graphics, new Rectangle(140, 111 + contentOffset, 130, 40), p[6], p[3], p[6], Spec.Finish, Math.Min(20, Spec.Radius), Spec.Finish == "flat" ? 0 : 1, LightMode);
        TextRenderer.DrawText(e.Graphics, "Подключение", Font, new Rectangle(140, 111 + contentOffset, 130, 40), ThemeSpec.Ink(p[6]), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using (Brush selected = new SolidBrush(p[7])) e.Graphics.FillRectangle(selected, 24, 94 + contentOffset, 70, 6);
        using (Brush muted = new SolidBrush(p[5])) { e.Graphics.FillRectangle(muted, 24, 116 + contentOffset, 54, 3); e.Graphics.FillRectangle(muted, 24, 135 + contentOffset, 62, 3); }
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { if (wallpaper != null) wallpaper.Dispose(); if (logo != null) logo.Dispose(); } base.Dispose(disposing); }
}

internal sealed class ThemeEditor : SessionForm
{
    internal ThemeSpec Result;
    private readonly TextBox name = new TextBox { Width = 380, MaxLength = 48 };
    private readonly ComboBox mode = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox finish = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown round = new NumericUpDown { Minimum = 8, Maximum = 32, Width = 110 };
    private readonly ThemePreview preview = new ThemePreview { Width = 480, Height = 220 };
    private readonly Label contrast = Theme.Caption("", 10, false);
    private readonly ColorChip[] chips = new ColorChip[9];
    internal ThemeEditor(ThemeSpec basis, bool copy)
    {
        Result = basis.Copy(); if (copy) { Result.Id = ""; Result.BuiltIn = false; Result.Name = "Моя тема"; }
        Text = copy ? "Создать тему" : "Изменить тему"; Font = new Font("Segoe UI", 10F); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 790); MinimumSize = new Size(720, 660); StartPosition = FormStartPosition.CenterParent;
        FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22) };
        content.Controls.Add(Theme.Caption("Своя тема", 22, true));
        name.Text = Result.Name; AddRow(content, "Название", name);
        finish.Items.AddRange(new object[] { "Матовая", "Liquid Glass", "Frutiger Aero" }); finish.SelectedIndex = Array.IndexOf(new[] { "flat", "glass", "aero" }, Result.Finish); AddRow(content, "Поверхность", finish);
        round.Value = Result.Radius; AddRow(content, "Скругление", round);
        mode.Items.AddRange(new object[] { "Светлый вариант", "Тёмный вариант" }); mode.SelectedIndex = Theme.Light ? 0 : 1; AddRow(content, "Редактировать", mode);
        content.Controls.Add(Theme.Caption("Нажми на цвет, чтобы изменить его. Настрой оба варианта — они переключаются солнцем и луной.", 10, false));
        TableLayoutPanel colors = new TableLayoutPanel { Width = 650, Height = 198, ColumnCount = 4, RowCount = 5 };
        colors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); colors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); colors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); colors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        for (int row = 0; row < 5; row++) colors.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        for (int i = 0; i < chips.Length; i++)
        {
            int index = i; int column = i < 5 ? 0 : 2; int row = i < 5 ? i : i - 5;
            Label label = Theme.Caption(ThemeSpec.Labels[i], 10, false); label.Margin = new Padding(0, 9, 0, 0);
            colors.Controls.Add(label, column, row);
            ColorChip chip = new ColorChip { Width = 124, Height = 32, AccessibleName = ThemeSpec.Labels[i] }; chips[i] = chip; colors.Controls.Add(chip, column + 1, row);
            chip.Click += delegate
            {
                using (ColorDialog dialog = new ColorDialog { Color = chip.Value, FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { (mode.SelectedIndex == 0 ? Result.Day : Result.Night)[index] = dialog.Color; RefreshPreview(); }
            };
        }
        AddArtworkControls(content);
        content.Controls.Add(colors); content.Controls.Add(preview); contrast.MaximumSize = new Size(645, 0); content.Controls.Add(contrast);
        FlowLayoutPanel actions = new FlowLayoutPanel { Width = 640, Height = 50 };
        Button save = new SoftButton { Text = "Сохранить тему", AutoSize = true }; Button cancel = new SoftButton { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(save); actions.Controls.Add(cancel); actions.Dock = DockStyle.Bottom; actions.Height = 58; actions.Padding = new Padding(22, 4, 0, 0); Controls.Add(content); Controls.Add(actions); CancelButton = cancel; AcceptButton = save;
        finish.SelectedIndexChanged += delegate { Result.Finish = new[] { "flat", "glass", "aero" }[finish.SelectedIndex]; RefreshPreview(); };
        round.ValueChanged += delegate { Result.Radius = (int)round.Value; RefreshPreview(); };
        mode.SelectedIndexChanged += delegate { RefreshPreview(); };
        save.Click += delegate
        {
            try
            {
                string value = name.Text.Trim(); if (value.Length == 0 || value.Any(char.IsControl)) throw new InvalidDataException("Введи название темы.");
                Result.Name = value;
                if (Result.Readability().StartsWith("Некоторый") && MessageBox.Show(this, Result.Readability() + " Сохранить?", "Контраст", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                ThemeStore.Save(Result); DialogResult = DialogResult.OK; Close();
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Не удалось сохранить тему", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        Theme.Apply(this); Theme.Primary(save); RefreshPreview();
    }
    private void AddArtworkControls(Control content)
    {
        FlowLayoutPanel pictures = new FlowLayoutPanel { Width = 650, Height = 100, WrapContents = true };
        Action<string, bool, bool> add = delegate(string title, bool logo, bool clear)
        {
            Button button = new SoftButton { Text = title, Width = 295, Height = 42 };
            button.Click += delegate
            {
                try
                {
                    byte[] data = null;
                    if (!clear) using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.ico", CheckFileExists = true })
                    { if (dialog.ShowDialog(this) != DialogResult.OK) return; data = ThemeArtwork.Import(dialog.FileName, logo); }
                    if (logo) Result.Logo = data; else { Result.Wallpaper = data; Result.LiveNight = false; }
                    RefreshPreview();
                }
                catch (Exception e) { MessageBox.Show(this, e.Message, "Изображение", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            pictures.Controls.Add(button);
        };
        add("Выбрать фон…", false, false); add("Стандартный фон", false, true);
        add("Выбрать значок…", true, false); add("Стандартный значок", true, true);
        content.Controls.Add(pictures);
        ComboBox fit = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        fit.Items.AddRange(new object[] { "Заполнить с обрезкой", "Вписать целиком", "Растянуть" });
        fit.SelectedIndex = Array.IndexOf(new[] { "cover", "contain", "stretch" }, Result.ImageFit);
        fit.SelectedIndexChanged += delegate { Result.ImageFit = new[] { "cover", "contain", "stretch" }[fit.SelectedIndex]; RefreshPreview(); };
        AddRow(content, "Размещение фона", fit);
        NumericUpDown opacity = new NumericUpDown { Minimum = 0, Maximum = 100, Value = Result.ImageOpacity, Width = 110 };
        opacity.ValueChanged += delegate { Result.ImageOpacity = (int)opacity.Value; RefreshPreview(); };
        AddRow(content, "Яркость фона, %", opacity);
        content.Controls.Add(Theme.Caption("Значок меняется в приложении, окнах и трее. Изображения входят в экспорт темы; исходные файлы можно переместить.", 10, false));
    }
    private static void AddRow(Control parent, string title, Control input)
    {
        FlowLayoutPanel row = new FlowLayoutPanel { Width = 650, Height = 38, WrapContents = false };
        Label label = Theme.Caption(title, 10, false); label.AutoSize = false; label.Width = 180; label.Margin = new Padding(0, 6, 0, 0);
        row.Controls.Add(label); row.Controls.Add(input); parent.Controls.Add(row);
    }
    private void RefreshPreview()
    {
        Color[] palette = mode.SelectedIndex == 0 ? Result.Day : Result.Night;
        for (int i = 0; i < chips.Length; i++) if (chips[i] != null) { chips[i].Value = palette[i]; chips[i].Invalidate(); }
        preview.Spec = Result; preview.LightMode = mode.SelectedIndex == 0; preview.Invalidate(); contrast.Text = Result.Readability();
    }
}

// All glass layers sample one shared, softly colored scene in form coordinates.
// No desktop capture, undocumented DWM flags, or per-pixel alpha on native HWNDs.
internal static class GlassScene
{
    private sealed class Cache : IDisposable
    {
        internal Bitmap Image, Small, Wallpaper;
        internal Size Size;
        internal int Revision = -1;
        public void Dispose()
        {
            if (Image != null) Image.Dispose(); if (Small != null) Small.Dispose();
            if (Wallpaper != null) Wallpaper.Dispose();
        }
    }
    private static readonly Dictionary<Form, Cache> caches = new Dictionary<Form, Cache>();
    private static Point Origin(Control control)
    {
        Point p = Point.Empty;
        while (control != null && !(control is Form)) { p.Offset(control.Left, control.Top); control = control.Parent; }
        return p;
    }
    private static void Blob(Graphics g, RectangleF rect, Color color)
    {
        using (GraphicsPath path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            using (PathGradientBrush brush = new PathGradientBrush(path))
            { brush.CenterColor = Color.FromArgb(190, color); brush.SurroundColors = new[] { Color.FromArgb(0, color) }; brush.FocusScales = new PointF(0.18F, 0.18F); g.FillPath(brush, path); }
        }
    }
    internal static Bitmap CreateScene(int width, int height, Color background, Color accent)
    {
        Bitmap image = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush baseFill = new LinearGradientBrush(new Rectangle(0, 0, width, height), background, Motion.Blend(background, accent, 0.22), 35F)) g.FillRectangle(baseFill, 0, 0, width, height);
            bool light = background.GetBrightness() > 0.5F;
            Color blue = light ? Color.FromArgb(97, 169, 239) : Color.FromArgb(20, 68, 121);
            Color lavender = light ? Color.FromArgb(193, 156, 235) : Color.FromArgb(81, 55, 119);
            Color peach = light ? Color.FromArgb(248, 187, 157) : Color.FromArgb(111, 58, 85);
            Color mint = light ? Color.FromArgb(131, 224, 213) : Color.FromArgb(26, 102, 106);
            Blob(g, new RectangleF(-width * .3F, -height * .35F, width * 1.05F, height * 1.3F), blue);
            Blob(g, new RectangleF(width * .27F, -height * .2F, width * .9F, height * .95F), lavender);
            Blob(g, new RectangleF(width * .57F, height * .34F, width * .8F, height * 1.05F), peach);
            Blob(g, new RectangleF(-width * .14F, height * .5F, width * .8F, height * .9F), mint);
        }
        return image;
    }
    private static Bitmap Scene(Form form)
    {
        Cache cache;
        if (!caches.TryGetValue(form, out cache))
        {
            cache = new Cache(); caches.Add(form, cache);
            form.Disposed += delegate { Cache entry; if (caches.TryGetValue(form, out entry)) { entry.Dispose(); caches.Remove(form); } };
        }
        Size size = form.ClientSize;
        size = new Size(Math.Max(1, size.Width), Math.Max(1, size.Height));
        if (cache.Small == null || cache.Revision != Theme.Revision)
        {
            Bitmap next = CreateScene(640, 480, Theme.Background, Theme.Accent);
            if (cache.Small != null) cache.Small.Dispose();
            if (cache.Wallpaper != null) cache.Wallpaper.Dispose();
            byte[] wallpaper = Theme.Current.WallpaperFor(Theme.Light);
            cache.Wallpaper = wallpaper == null ? null : ThemeArtwork.Decode(wallpaper);
            cache.Small = next; cache.Revision = Theme.Revision; cache.Size = Size.Empty;
        }
        if (cache.Image == null || cache.Size != size)
        {
            if (cache.Image == null || cache.Image.Size != size)
            {
                if (cache.Image != null) cache.Image.Dispose();
                cache.Image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            }
            using (Graphics g = Graphics.FromImage(cache.Image))
            using (ImageAttributes edges = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                edges.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(cache.Small, new Rectangle(Point.Empty, size), 0, 0,
                    cache.Small.Width, cache.Small.Height, GraphicsUnit.Pixel, edges);
                if (cache.Wallpaper != null) ThemeArtwork.DrawBackground(g, new Rectangle(Point.Empty, size), Theme.Current, cache.Wallpaper);
            }
            cache.Size = size;
        }
        return cache.Image;
    }
    internal static void Backdrop(Graphics g, Control control, Rectangle bounds, Point shift)
    {
        GraphicsState state = g.Save();
        try { g.TranslateTransform(shift.X, shift.Y); Backdrop(g, control, bounds); }
        finally { g.Restore(state); }
    }
    internal static void Backdrop(Graphics g, Control control, Rectangle bounds)
    {
        if (bounds.Width < 1 || bounds.Height < 1) return;
        Form form = control.FindForm();
        if (form == null) { using (Brush fill = new SolidBrush(Theme.Background)) g.FillRectangle(fill, bounds); return; }
        Point origin = Origin(control); GraphicsState state = g.Save();
        try
        {
            g.SetClip(bounds, CombineMode.Intersect);
            Bitmap scene = Scene(form);
            Rectangle source = new Rectangle(origin.X + bounds.X, origin.Y + bounds.Y, bounds.Width, bounds.Height);
            source.Intersect(new Rectangle(Point.Empty, scene.Size));
            if (source.Width > 0 && source.Height > 0)
                g.DrawImage(scene, new Rectangle(source.X - origin.X, source.Y - origin.Y, source.Width, source.Height),
                    source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel);
            Control layer = control;
            while (layer != null && !(layer is Card) && (string)layer.Tag != "soft-panel") layer = layer.Parent;
            if (layer != null)
            {
                Point anchor = Origin(layer);
                Rectangle surface = new Rectangle(anchor.X - origin.X, anchor.Y - origin.Y, Math.Max(2, layer.Width), Math.Max(2, layer.Height));
                using (Brush veil = new SolidBrush(Color.FromArgb((Theme.Liquid ? (int)(120 + 38 * Theme.Lightness) : Theme.Aero ? (Theme.Light ? 242 : 237) : 255), Theme.Panel))) g.FillRectangle(veil, bounds);
                using (LinearGradientBrush sheen = new LinearGradientBrush(surface, Color.FromArgb((Theme.Liquid ? (int)(12 + 10 * Theme.Lightness) : Theme.Aero ? 12 : 0), Color.White), Color.FromArgb(0, Color.White), 125F)) g.FillRectangle(sheen, bounds);
            }
        }
        finally { g.Restore(state); }
    }
    internal static void Rim(Graphics g, Rectangle r, int radius)
    {
        if (r.Width < 5 || r.Height < 5) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = Theme.Round(r, radius))
        {
            using (Pen shadow = new Pen(Color.FromArgb(24, Color.Black), 3)) g.DrawPath(shadow, path);
            using (LinearGradientBrush light = new LinearGradientBrush(r, Color.FromArgb(170, Color.White), Color.FromArgb(24, Color.White), 65F))
            using (Pen edge = new Pen(light, 1.2F)) g.DrawPath(edge, path);
        }
        using (GraphicsPath inner = Theme.Round(Rectangle.Inflate(r, -2, -2), Math.Max(2, radius - 2)))
        using (Pen line = new Pen(Color.FromArgb(34, Color.White))) g.DrawPath(line, inner);
    }
    internal static void Surface(Graphics g, Rectangle r, Color fill, int radius, double strength)
    {
        if (r.Width < 3 || r.Height < 3) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = Theme.Round(r, radius))
        {
            bool tinted = fill.ToArgb() == Theme.Accent.ToArgb();
            using (Brush veil = new SolidBrush(Color.FromArgb(tinted ? 226 : 50, fill))) g.FillPath(veil, path);
            using (LinearGradientBrush highlight = new LinearGradientBrush(r, Color.FromArgb((int)(34 * strength), Color.White), Color.FromArgb(0, Color.White), 115F)) g.FillPath(highlight, path);
        }
        Rim(g, r, radius);
    }
    internal static void Glint(Graphics g, Rectangle r, int radius, PointF position)
    {
        if (r.Width < 4 || r.Height < 4) return;
        GraphicsState state = g.Save();
        try
        {
            using (GraphicsPath clip = Theme.Round(r, radius)) g.SetClip(clip, CombineMode.Intersect);
            float diameter = Math.Max(60, r.Width * .85F);
            using (GraphicsPath ellipse = new GraphicsPath())
            {
                ellipse.AddEllipse(r.X + r.Width * position.X - diameter / 2, r.Y + r.Height * position.Y - diameter / 2, diameter, diameter);
                using (PathGradientBrush light = new PathGradientBrush(ellipse))
                { light.CenterColor = Color.FromArgb(38, Color.White); light.SurroundColors = new[] { Color.FromArgb(0, Color.White) }; g.FillPath(light, ellipse); }
            }
        }
        finally { g.Restore(state); }
    }
}

internal static class ThemeArtwork
{
    internal static Bitmap Decode(byte[] data)
    {
        using (MemoryStream stream = new MemoryStream(data))
        using (Image source = Image.FromStream(stream, true, true))
        {
            if (source.Width > 8192 || source.Height > 8192 || (long)source.Width * source.Height > 36000000)
                throw new InvalidDataException("Изображение слишком большое: максимум 36 мегапикселей и 8192 по стороне.");
            return new Bitmap(source);
        }
    }
    internal static byte[] Read(XElement node, bool logo)
    {
        if (node == null) return null;
        byte[] data = Convert.FromBase64String(node.Value);
        if (data.Length == 0 || data.Length > (logo ? 512 * 1024 : 4 * 1024 * 1024)) throw new InvalidDataException("Слишком большое изображение в теме.");
        using (Bitmap check = Decode(data))
            if (check.Width > (logo ? 256 : 1920) || check.Height > (logo ? 256 : 1080)) throw new InvalidDataException("Недопустимый размер изображения темы.");
        return data;
    }
    internal static byte[] Import(string path, bool logo)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidDataException("Выбери изображение меньше 20 МБ.");
        using (Bitmap source = LoadFile(path))
        {
            double scale = Math.Min(1, Math.Min((logo ? 256.0 : 1920.0) / source.Width, (logo ? 256.0 : 1080.0) / source.Height));
            int w = logo ? 256 : Math.Max(1, (int)(source.Width * scale));
            int h = logo ? 256 : Math.Max(1, (int)(source.Height * scale));
            using (Bitmap image = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            using (MemoryStream output = new MemoryStream())
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(logo ? Color.Transparent : Color.Black); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    int dw = Math.Max(1, (int)(source.Width * scale)), dh = Math.Max(1, (int)(source.Height * scale));
                    g.DrawImage(source, new Rectangle((w - dw) / 2, (h - dh) / 2, dw, dh));
                }
                if (logo) image.Save(output, ImageFormat.Png);
                else
                {
                    ImageCodecInfo encoder = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
                    using (EncoderParameters options = new EncoderParameters(1))
                    { options.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L); image.Save(output, encoder, options); }
                }
                byte[] result = output.ToArray();
                if (result.Length > (logo ? 512 * 1024 : 4 * 1024 * 1024)) throw new InvalidDataException("Изображение не удалось уменьшить до размера темы.");
                return result;
            }
        }
    }
    private static Bitmap LoadFile(string path)
    {
        if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            using (Icon icon = new Icon(path, new Size(256, 256))) return icon.ToBitmap();
        return Decode(File.ReadAllBytes(path));
    }
    internal static void PaintBackground(Graphics g, Rectangle bounds, ThemeSpec spec, bool light)
    {
        if (spec.Wallpaper == null || bounds.Width < 1 || bounds.Height < 1) return;
        using (Bitmap image = Decode(spec.Wallpaper))
            DrawBackground(g, bounds, spec, image);
    }
    internal static void DrawBackground(Graphics g, Rectangle bounds, ThemeSpec spec, Bitmap image, bool fast = false)
        {
            GraphicsState saved = g.Save();
            try
            {
                g.SetClip(bounds, CombineMode.Intersect); g.InterpolationMode = fast ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
                Rectangle target = bounds;
                if (spec.ImageFit != "stretch")
                {
                    double x = bounds.Width / (double)image.Width, y = bounds.Height / (double)image.Height;
                    double scale = spec.ImageFit == "contain" ? Math.Min(x, y) : Math.Max(x, y);
                    int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
                    target = new Rectangle(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
                }
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    ColorMatrix alpha = new ColorMatrix(); alpha.Matrix33 = spec.ImageOpacity / 100F; attributes.SetColorMatrix(alpha);
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            finally { g.Restore(saved); }
    }
}

internal sealed class GlassListBox : ListBox
{
    internal GlassListBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Theme.SceneBackground) GlassScene.Backdrop(e.Graphics, this, ClientRectangle);
        else e.Graphics.Clear(BackColor);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        // Paint the entire visible list in one buffer. Native selection,
        // keyboard navigation and scrolling still belong to ListBox.
        for (int i = Math.Max(0, TopIndex); i < Items.Count; i++)
        {
            Rectangle row = GetItemRectangle(i);
            if (row.Top >= ClientSize.Height) break;
            if (!row.IntersectsWith(e.ClipRectangle)) continue;
            DrawItemState state = i == SelectedIndex ? DrawItemState.Selected : DrawItemState.None;
            if (i == SelectedIndex && Focused) state |= DrawItemState.Focus;
            if (!Enabled) state |= DrawItemState.Disabled;
            OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, row, i, state, ForeColor, BackColor));
        }
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Native list scrolling reuses old pixels, but a glass backdrop is fixed
        // in form coordinates and must be repainted at the new scroll position.
        if (Theme.SceneBackground && (m.Msg == 0x0115 || m.Msg == 0x020A || m.Msg == 0x0005)) Invalidate();
    }
}

// Only this opaque, double-buffered surface changes during a theme transition.
// Native child windows are restyled once, not independently on every frame.
internal sealed class ThemeFade : Control
{
    internal static bool Running { get { return active.Count > 0; } }
    private static readonly Dictionary<Form, ThemeFade> active = new Dictionary<Form, ThemeFade>();
    private readonly Form form;
    private readonly Bitmap before, after;
    private readonly AnimationFrame frame = new AnimationFrame();
    private readonly Stopwatch watch = new Stopwatch();
    private double progress;
    private ThemeFade(Form owner, Bitmap start, Bitmap end)
    {
        form = owner; before = start; after = end;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        TabStop = false; Bounds = owner.ClientRectangle; Anchor = AnchorStyles.Top | AnchorStyles.Left;
        frame.Tick += delegate
        {
            double t = Math.Min(1, watch.Elapsed.TotalMilliseconds / 320.0);
            progress = t * t * (3 - 2 * t);
            if (t >= 1) Dispose(); else Invalidate();
        };
        owner.SizeChanged += Finish; owner.Disposed += Finish;
    }
    private void Finish(object sender, EventArgs e) { Dispose(); }
    private static Bitmap CaptureWindow(Form form)
    {
        if (form.ClientSize.Width < 1 || form.ClientSize.Height < 1 ||
            (long)form.ClientSize.Width * form.ClientSize.Height > 12000000) return null;
        Bitmap result = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using (Graphics g = Graphics.FromImage(result)) g.Clear(form.BackColor);
            for (int i = form.Controls.Count - 1; i >= 0; i--)
            {
                Control child = form.Controls[i];
                if (child.Visible && !(child is ThemeFade) && child.Width > 0 && child.Height > 0)
                    child.DrawToBitmap(result, child.Bounds);
            }
            return result;
        }
        catch { result.Dispose(); return null; } // Unsupported native controls: switch instantly.
    }
    internal static Dictionary<Form, Bitmap> CaptureAll()
    {
        Dictionary<Form, Bitmap> result = new Dictionary<Form, Bitmap>();
        foreach (Form form in Application.OpenForms)
        {
            if (!form.Visible || form.IsDisposed || form.WindowState == FormWindowState.Minimized) continue;
            ThemeFade fade; Bitmap bitmap;
            if (active.TryGetValue(form, out fade))
            {
                bitmap = new Bitmap(fade.Width, fade.Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(bitmap)) fade.Render(g);
            }
            else bitmap = CaptureWindow(form);
            if (bitmap != null) result.Add(form, bitmap);
        }
        return result;
    }
    internal static void Play(Dictionary<Form, Bitmap> snapshots)
    {
        foreach (KeyValuePair<Form, Bitmap> entry in snapshots)
        {
            Form owner = entry.Key;
            if (owner.IsDisposed || !owner.Visible || owner.ClientSize != entry.Value.Size) continue;
            Bitmap target = CaptureWindow(owner);
            if (target == null) continue;
            ThemeFade fade = new ThemeFade(owner, (Bitmap)entry.Value.Clone(), target);
            active.Add(owner, fade); owner.Controls.Add(fade); fade.BringToFront();
        }
        // Start after all captures, so a second open window cannot steal frames.
        foreach (ThemeFade fade in active.Values) { fade.watch.Restart(); fade.frame.Start(); }
    }
    internal static void FinishAll()
    {
        ThemeFade[] fades = active.Values.ToArray();
        foreach (ThemeFade fade in fades) fade.Dispose();
    }
    private void Render(Graphics g)
    {
        g.DrawImageUnscaled(before, 0, 0);
        using (ImageAttributes attributes = new ImageAttributes())
        {
            ColorMatrix alpha = new ColorMatrix(); alpha.Matrix33 = (float)progress; attributes.SetColorMatrix(alpha);
            g.DrawImage(after, ClientRectangle, 0, 0, after.Width, after.Height, GraphicsUnit.Pixel, attributes);
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { Render(e.Graphics); }
    protected override void WndProc(ref Message m)
    {
        // The visual overlay must not swallow clicks or change keyboard focus.
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            frame.Dispose(); form.SizeChanged -= Finish; form.Disposed -= Finish;
            active.Remove(form); before.Dispose(); after.Dispose();
        }
        base.Dispose(disposing);
        if (disposing && !form.IsDisposed && !form.Disposing) form.Invalidate(true);
    }
}

// A silent, embedded frame loop. No external player, codec, network request or
// per-frame file access. Decode one frame shared by all visible windows.
