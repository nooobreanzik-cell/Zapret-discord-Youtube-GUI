using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class ThemeSwitch : CheckBox
{
    private readonly SmoothValue slide;
    internal ThemeSwitch()
    {
        Size = new Size(116, 36); Margin = new Padding(0, 0, 0, 12); Cursor = Cursors.Hand;
        AccessibleName = "Светлая тема"; AccessibleDescription = "Солнце — светлая тема, луна — тёмная. Пробел переключает тему.";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        slide = new SmoothValue(this);
    }
    protected override void OnCheckedChanged(EventArgs e) { if (slide != null) slide.Set(Checked ? 1 : 0); base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent == null ? Theme.Panel : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath track = Theme.Round(new Rectangle(1, 1, Width-3, Height-3), 16))
        using (Brush fill = new SolidBrush(Theme.Raised))
        using (Pen border = new Pen(Focused && ShowFocusCues ? Theme.Accent : Theme.Border))
        { e.Graphics.FillPath(fill, track); e.Graphics.DrawPath(border, track); }
        int x = (int)(4 + 57 * (1-slide.Value));
        using (GraphicsPath selected = Theme.Round(new Rectangle(x, 4, 50, 27), 12))
        using (Brush fill = new SolidBrush(Theme.Selected)) e.Graphics.FillPath(fill, selected);
        using (Pen sun = new Pen(Checked ? Theme.Accent : Theme.Muted, 1.7F))
        {
            e.Graphics.DrawEllipse(sun, 23, 12, 10, 10);
            for (int i = 0; i < 8; i++) { double a = i*Math.PI/4; e.Graphics.DrawLine(sun, (float)(28+8*Math.Cos(a)), (float)(17+8*Math.Sin(a)), (float)(28+11*Math.Cos(a)), (float)(17+11*Math.Sin(a))); }
        }
        using (Brush moon = new SolidBrush(!Checked ? Theme.Accent : Theme.Muted)) e.Graphics.FillEllipse(moon, 78, 8, 17, 17);
        using (Brush cut = new SolidBrush(!Checked ? Theme.Selected : Theme.Raised)) e.Graphics.FillEllipse(cut, 84, 5, 16, 16);
    }
}

internal sealed class CreatorsWindow : SessionForm
{
    internal CreatorsWindow()
    {
        Text = "Создатели — Zapret GUI"; Font = new Font("Segoe UI", 10F); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(730, 700); MinimumSize = new Size(750, 620); StartPosition = FormStartPosition.CenterParent;
        FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Tag = "canvas" };
        content.Controls.Add(Theme.Caption("Создатели", 24, true));
        content.Controls.Add(Theme.Caption("Люди и инструменты, благодаря которым появилась оболочка ZT.", 10, false));
        AddCard(content, "nooobreanzik-cell", "Автор идеи и направления проекта. Требования к интерфейсу, проверка на Windows и обратная связь.", "Открыть GitHub", "https://github.com/nooobreanzik-cell");
        AddCard(content, "ChatGPT · OpenAI", "ИИ-помощник в разработке: код, оформление и разбор ошибок. Проект создан совместно с пользователем и не является официальным продуктом OpenAI.", "Открыть ChatGPT", "https://chatgpt.com/");
        AddCard(content, "Авторы используемых компонентов", "Flowseal и bol-van — сборка и движок Zapret. V3nilla — списки для hosts и IPSet. Спасибо авторам за их работу.", "Сборка Flowseal", "https://github.com/Flowseal/zapret-discord-youtube");
        Controls.Add(content); Theme.Apply(this);
    }
    private void AddCard(Control parent, string name, string description, string linkText, string url)
    {
        Card card = new Card { Width = 650, Height = 196, Margin = new Padding(0, 0, 0, 14) };
        FlowLayoutPanel rows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        rows.Controls.Add(Theme.Caption(name, 14, true));
        Label body = Theme.Caption(description, 10, false); body.MaximumSize = new Size(604, 0); rows.Controls.Add(body);
        Button link = new SoftButton { Text = linkText, AutoSize = true };
        link.Click += delegate { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception error) { MessageBox.Show(this, error.Message, "Не удалось открыть ссылку"); } };
        rows.Controls.Add(link); card.Controls.Add(rows); parent.Controls.Add(card);
    }
}
