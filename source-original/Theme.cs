using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Theme
{
    internal static bool Light;
    internal static event Action Changed;
    private static readonly HashSet<Control> wired = new HashSet<Control>();
    internal static void Switch(bool light)
    {
        Light = light;
        foreach (Form form in Application.OpenForms) { form.SuspendLayout(); Apply(form); DarkTitle(form); form.ResumeLayout(true); form.Invalidate(true); }
        if (Changed != null) Changed();
    }
    internal static Color Background { get { return Light ? Color.FromArgb(244, 248, 247) : Color.FromArgb(15, 26, 33); } }
    internal static Color Panel { get { return Light ? Color.FromArgb(255, 255, 255) : Color.FromArgb(23, 44, 54); } }
    internal static Color Raised { get { return Light ? Color.FromArgb(233, 241, 237) : Color.FromArgb(32, 56, 66); } }
    internal static Color Border { get { return Light ? Color.FromArgb(196, 214, 204) : Color.FromArgb(49, 75, 85); } }
    internal static Color Text { get { return Light ? Color.FromArgb(24, 46, 39) : Color.FromArgb(241, 249, 246); } }
    internal static Color Muted { get { return Light ? Color.FromArgb(79, 105, 93) : Color.FromArgb(171, 192, 199); } }
    internal static Color Accent { get { return Light ? Color.FromArgb(22, 115, 80) : Color.FromArgb(114, 223, 176); } }
    internal static Color Selected { get { return Light ? Color.FromArgb(215, 239, 226) : Color.FromArgb(35, 75, 68); } }
    internal static Color Error { get { return Light ? Color.FromArgb(168, 40, 42) : Color.FromArgb(255, 164, 160); } }
    internal static Label Caption(string text, int size, bool strong)
    {
        return new Label { Text = text, Tag = strong ? "strong" : "muted", AutoSize = true, ForeColor = strong ? Text : Muted,
            Font = new Font("Segoe UI", size, strong ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 0, 0, 10), MaximumSize = new Size(670, 0) };
    }
    internal static void Apply(Control control)
    {
        control.BackColor = control is Form || (string)control.Tag == "canvas" ? Background : Panel;
        control.ForeColor = (string)control.Tag == "muted" ? Muted : Text;
        if (control is Label) control.BackColor = Color.Transparent;
        TextBox box = control as TextBox;
        if (box != null) { box.BackColor = Background; box.BorderStyle = BorderStyle.FixedSingle; }
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
            EventHandler shape = delegate { Clip(control, 7); };
            control.SizeChanged += shape; control.HandleCreated += shape; Clip(control, 7);
            control.HandleCreated += delegate { try { SetWindowTheme(control.Handle, Light ? "Explorer" : "DarkMode_Explorer", null); } catch { } };
            control.Disposed += delegate { wired.Remove(control); };
        }
        if (control.IsHandleCreated && (control is TextBox || control is ComboBox || control is ListBox))
            try { SetWindowTheme(control.Handle, Light ? "Explorer" : "DarkMode_Explorer", null); } catch { }
        if (button != null && (string)button.Tag == "primary") Primary(button);
        if (button != null && (string)button.Tag == "selected") { button.BackColor = Selected; button.ForeColor = Accent; }
        foreach (Control child in control.Controls) Apply(child);
    }
    internal static void Primary(Button button)
    {
        button.Tag = "primary"; button.BackColor = Accent; button.ForeColor = Light ? Color.White : Background;
        button.FlatAppearance.BorderColor = Accent;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(148, 237, 196);
        button.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
    }
    internal static void DarkTitle(Form form)
    {
        try { int enabled = Light ? 0 : 1; DwmSetWindowAttribute(form.Handle, 20, ref enabled, 4); int rounded = 2; DwmSetWindowAttribute(form.Handle, 33, ref rounded, 4); } catch { }
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
        using (GraphicsPath path = Round(new Rectangle(0, 0, control.Width, control.Height), radius)) control.Region = new Region(path);
        if (previous != null) previous.Dispose();
    }
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string app, string id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

internal sealed class Card : Panel
{
    internal Card() { SetStyle(ControlStyles.ResizeRedraw, true); DoubleBuffered = true; BackColor = Theme.Panel; Padding = new Padding(20); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
        if (Width < 25 || Height < 25) return;
        using (GraphicsPath path = new GraphicsPath())
        {
            int d = 24;
            path.AddArc(0, 0, d, d, 180, 90); path.AddArc(Width-d-1, 0, d, d, 270, 90);
            path.AddArc(Width-d-1, Height-d-1, d, d, 0, 90); path.AddArc(0, Height-d-1, d, d, 90, 90); path.CloseFigure();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush fill = new SolidBrush(Theme.Panel)) e.Graphics.FillPath(fill, path);
            using (Pen line = new Pen(Theme.Border)) e.Graphics.DrawPath(line, path);
        }
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
    private readonly SmoothValue hover;
    internal SoftButton()
    {
        hover = new SmoothValue(this);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; hover.Set(1); base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; hover.Set(0); base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Color parent = Parent == null ? Theme.Panel : Parent.BackColor;
        e.Graphics.Clear(parent); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        bool primary = BackColor == Theme.Accent;
        Color fill = !Enabled ? Theme.Panel : pressed ? Theme.Selected : Motion.Blend(BackColor, primary ? (Theme.Light ? Color.FromArgb(30,137,96) : Color.FromArgb(148,237,196)) : Theme.Light ? Color.FromArgb(215,230,221) : Color.FromArgb(45,75,86), hover.Value);
        using (GraphicsPath path = Theme.Round(new Rectangle(1, 1, Math.Max(1, Width-3), Math.Max(1, Height-3)), 10))
        using (Brush brush = new SolidBrush(fill))
        using (Pen border = new Pen(Focused && ShowFocusCues ? Theme.Accent : primary && Enabled ? Theme.Accent : Theme.Border))
        { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(border, path); }
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
        e.Graphics.Clear(Parent == null ? Theme.Panel : Parent.BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int y = (Height - 18) / 2;
        using (GraphicsPath track = Theme.Round(new Rectangle(1, y, 30, 18), 9))
        using (Brush fill = new SolidBrush(Motion.Blend(Theme.Border, Theme.Accent, slide.Value))) e.Graphics.FillPath(fill, track);
        using (Brush knob = new SolidBrush(Checked ? Theme.Background : Theme.Muted)) e.Graphics.FillEllipse(knob, (float)(4 + 12 * slide.Value), y + 3, 12, 12);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(40, 0, Math.Max(1,Width-42), Height), Enabled ? ForeColor : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        if (Focused && ShowFocusCues) using (Pen pen = new Pen(Theme.Accent)) e.Graphics.DrawLine(pen, 40, Height-2, Width-3, Height-2);
    }
}
