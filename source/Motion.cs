using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

internal static class Motion
{
    private static bool enabled = true;
    internal static event Action Changed;
    internal static event Action Finishing;
    internal static void FinishTransitions() { if (Finishing != null) Finishing(); }
    internal static bool Enabled { get { return enabled; } set { enabled = value; if (Changed != null) Changed(); } }
    internal static Color Blend(Color a, Color b, double t)
    { return Color.FromArgb((int)(a.R+(b.R-a.R)*t), (int)(a.G+(b.G-a.G)*t), (int)(a.B+(b.B-a.B)*t)); }
    internal static void Show(Form form)
    {
        if (!Enabled || !SystemInformation.UIEffectsEnabled) return;
        AnimationFrame timer = new AnimationFrame(); Stopwatch watch = Stopwatch.StartNew(); form.Opacity = 0.90;
        Action settle = null;
        EventHandler closed = null;
        closed = delegate { timer.Dispose(); Changed -= settle; form.Disposed -= closed; };
        settle = delegate { if (!Enabled) { if (!form.IsDisposed) form.Opacity = 1; closed(form, EventArgs.Empty); } };
        Changed += settle; form.Disposed += closed;
        timer.Tick += delegate
        {
            if (form.IsDisposed) { closed(form, EventArgs.Empty); return; }
            double t = Enabled ? Math.Min(1, watch.Elapsed.TotalMilliseconds / 160) : 1;
            form.Opacity = 0.90 + 0.10 * (1-Math.Pow(1-t,3));
            if (t >= 1) closed(form, EventArgs.Empty);
        };
        timer.Start();
    }
}
internal sealed class SmoothValue : IDisposable
{
    private readonly Control owner;
    private readonly AnimationFrame timer = new AnimationFrame();
    private readonly Stopwatch watch = new Stopwatch();
    private double value, from, target;
    internal SmoothValue(Control control)
    {
        owner = control; timer.Tick += delegate { Step(); }; Motion.Changed += Settle; Motion.Finishing += Finish;
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
    private void Settle() { if (!Motion.Enabled) Finish(); }
    private void Finish() { value = target; timer.Stop(); if (!owner.IsDisposed) owner.Invalidate(); }
    public void Dispose() { timer.Dispose(); Motion.Changed -= Settle; Motion.Finishing -= Finish; }
}

// One UI-thread clock for all active animations. Request a short interval;
// actual cadence depends on Windows and rendering load. Elapsed time controls progress.
// Nothing runs when idle and callbacks never accumulate in the UI message queue.
internal sealed class AnimationFrame : IDisposable
{
    private static readonly Timer clock = new Timer { Interval = 10 };
    private static readonly HashSet<AnimationFrame> active = new HashSet<AnimationFrame>();
    internal event Action Tick;
    internal bool Enabled { get; private set; }
    private bool disposed;
    static AnimationFrame()
    {
        clock.Tick += delegate
        {
            AnimationFrame[] frames = new AnimationFrame[active.Count]; active.CopyTo(frames);
            foreach (AnimationFrame frame in frames)
                if (frame.Enabled && frame.Tick != null) frame.Tick();
        };
        Application.ApplicationExit += delegate { clock.Stop(); active.Clear(); };
    }
    internal void Start() { if (disposed || Enabled) return; Enabled = true; active.Add(this); clock.Start(); }
    internal void Stop() { Enabled = false; active.Remove(this); if (active.Count == 0) clock.Stop(); }
    public void Dispose() { if (disposed) return; Stop(); Tick = null; disposed = true; }
}
