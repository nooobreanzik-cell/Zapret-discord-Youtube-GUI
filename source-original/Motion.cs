using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

internal static class Motion
{
    private static bool enabled = true;
    internal static event Action Changed;
    internal static bool Enabled { get { return enabled; } set { enabled = value; if (Changed != null) Changed(); } }
    internal static Color Blend(Color a, Color b, double t)
    { return Color.FromArgb((int)(a.R+(b.R-a.R)*t), (int)(a.G+(b.G-a.G)*t), (int)(a.B+(b.B-a.B)*t)); }
    internal static void Show(Form form)
    {
        if (!Enabled || !SystemInformation.UIEffectsEnabled) return;
        Timer timer = new Timer { Interval = 16 }; Stopwatch watch = Stopwatch.StartNew(); form.Opacity = 0.90;
        timer.Tick += delegate
        {
            if (form.IsDisposed) { timer.Dispose(); return; }
            double t = Enabled ? Math.Min(1, watch.Elapsed.TotalMilliseconds / 160) : 1;
            form.Opacity = 0.90 + 0.10 * (1-Math.Pow(1-t,3));
            if (t >= 1) { timer.Stop(); timer.Dispose(); }
        };
        timer.Start();
    }
}
internal sealed class SmoothValue : IDisposable
{
    private readonly Control owner;
    private readonly Timer timer = new Timer { Interval = 16 };
    private readonly Stopwatch watch = new Stopwatch();
    private double value, from, target;
    internal SmoothValue(Control control)
    {
        owner = control; timer.Tick += delegate { Step(); }; Motion.Changed += Settle;
        owner.Disposed += delegate { Dispose(); };
    }
    internal double Value { get { return value; } }
    internal void Set(double next)
    {
        if (target == next) return;
        from = value; target = next;
        if (!Motion.Enabled || !SystemInformation.UIEffectsEnabled) { value = target; timer.Stop(); owner.Invalidate(); return; }
        watch.Restart(); timer.Start();
    }
    private void Step()
    {
        double t = Math.Min(1, watch.Elapsed.TotalMilliseconds / 140);
        value = from + (target-from)*(1-Math.Pow(1-t,3)); owner.Invalidate();
        if (t >= 1) timer.Stop();
    }
    private void Settle() { if (Motion.Enabled) return; value = target; timer.Stop(); if (!owner.IsDisposed) owner.Invalidate(); }
    public void Dispose() { timer.Dispose(); Motion.Changed -= Settle; }
}
