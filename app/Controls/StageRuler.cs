using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TestingApp.Models;
using TestingApp.ViewModels;
using Ellipse = System.Windows.Shapes.Ellipse;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace TestingApp.Controls;

// The frame-buffer ruler: 0.25f ... 32f (1 ... 128 ticks) on a log scale.
// Round dots = the fixed stages (click to select), squares A-D = the custom slots (click to select, drag to change their delay).
public sealed class StageRuler : Canvas
{
    const double PadL = 30, PadR = 30, TrackY = 66;
    static readonly Brush Blue = Make("#2F6BFF"), Rail = Make("#E3E9F5"), Grey = Make("#B4C0D6"), Ink = Make("#1B2433"), Muted = Make("#6B7790"), White = Brushes.White;

    BufferPage? _page;
    int _dragSlot = -1;
    double _dragTicks;

    static Brush Make(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public StageRuler()
    {
        Height = 126;
        Background = Brushes.Transparent;
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => Redraw();
        MouseMove += OnMove;
        MouseLeftButtonUp += (_, _) => EndDrag();
        LostMouseCapture += (_, _) => EndDrag();
    }

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_page != null) _page.PropertyChanged -= OnPageChanged;
        _page = e.NewValue as BufferPage;
        if (_page != null) _page.PropertyChanged += OnPageChanged;
        Redraw();
    }

    void OnPageChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BufferPage.Version)) Redraw();
    }

    double Span => Math.Log2(Math.Max(2, _page?.Schema.CustomMaxTicks ?? 128));
    double Usable => Math.Max(10, ActualWidth - PadL - PadR);
    double X(double ticks) => PadL + Math.Log2(Math.Clamp(ticks, 1, Math.Pow(2, Span))) / Span * Usable;
    double TicksAt(double x) => Math.Pow(2, Math.Clamp((x - PadL) / Usable, 0, 1) * Span);

    T Add<T>(T element, double left, double top) where T : UIElement
    {
        SetLeft(element, left);
        SetTop(element, top);
        Children.Add(element);
        return element;
    }

    TextBlock Label(string text, double centerX, double top, Brush brush, double size = 11, FontWeight? weight = null)
    {
        var tb = new TextBlock { Text = text, Foreground = brush, FontSize = size, FontWeight = weight ?? FontWeights.Normal, IsHitTestVisible = false };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Add(tb, centerX - tb.DesiredSize.Width / 2, top);
        return tb;
    }

    void Redraw()
    {
        Children.Clear();
        if (_page == null || ActualWidth < 80) return;
        var page = _page;
        bool on = !page.IsOff;
        int selected = page.Stage;
        double selTicks = on ? page.TicksOf(selected) : 0;
        if (_dragSlot >= 0 && page.SlotOfStage(selected) is { } sel && sel.Spec.Slot == _dragSlot) selTicks = _dragTicks;

        // rail and active part
        Add(new Rectangle { Width = Usable, Height = 6, RadiusX = 3, RadiusY = 3, Fill = Rail, IsHitTestVisible = false }, PadL, TrackY - 3);
        if (on && selTicks > 0)
            Add(new Rectangle { Width = Math.Max(6, X(selTicks) - PadL), Height = 6, RadiusX = 3, RadiusY = 3, Fill = Blue, IsHitTestVisible = false }, PadL, TrackY - 3);

        // fixed stages
        foreach (var st in page.Schema.Stages.Where(s => s.Enabled && !s.Custom))
        {
            bool isSel = on && st.Stage == selected;
            double size = isSel ? 20 : 14;
            int stage = st.Stage;
            var dot = new Ellipse
            {
                Width = size, Height = size, Fill = isSel ? Blue : White, Stroke = isSel ? Blue : Grey, StrokeThickness = 2.5,
                Cursor = page.CanEdit ? Cursors.Hand : Cursors.Arrow, ToolTip = Fmt.Frames(st.Ticks) + " · " + st.Ticks + " ticks",
            };
            dot.MouseLeftButtonDown += (_, e) => { if (page.CanEdit) page.SelectStage(stage); e.Handled = true; };
            Add(dot, X(st.Ticks) - size / 2, TrackY - size / 2);
            Label(Fmt.Frames(st.Ticks), X(st.Ticks), TrackY + 18, isSel ? Blue : Muted, 11, isSel ? FontWeights.SemiBold : FontWeights.Normal);
        }
        Label(Fmt.Frames(Math.Pow(2, Span)), X(Math.Pow(2, Span)), TrackY + 18, Muted);

        // custom slots
        foreach (var slot in page.Slots)
        {
            bool isSel = on && slot.Spec.Stage == selected;
            double ticks = _dragSlot == slot.Spec.Slot ? _dragTicks : slot.Ticks.NumValue;
            double cx = X(ticks);
            Add(new Rectangle { Width = 2, Height = 20, Fill = isSel ? Blue : Grey, IsHitTestVisible = false }, cx - 1, 38);
            var box = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(9), Background = isSel ? Blue : White,
                BorderBrush = Blue, BorderThickness = new Thickness(2), Cursor = page.CanEdit ? Cursors.SizeWE : Cursors.Arrow,
                ToolTip = "Custom " + slot.Letter + ": " + Fmt.Frames(ticks) + " · " + Fmt.Num(ticks) + " ticks (drag to change)",
                Child = new TextBlock { Text = slot.Letter, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = isSel ? White : Blue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var captured = slot;
            box.MouseLeftButtonDown += (_, e) =>
            {
                if (!page.CanEdit) return;
                page.SelectStage(captured.Spec.Stage);
                _dragSlot = captured.Spec.Slot;
                _dragTicks = captured.Ticks.NumValue;
                CaptureMouse();
                e.Handled = true;
            };
            Add(box, cx - 14, 8);
        }
        Opacity = on ? 1 : 0.5;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragSlot < 0 || _page == null || e.LeftButton != MouseButtonState.Pressed) return;
        _dragTicks = Math.Round(TicksAt(e.GetPosition(this).X));
        Redraw();
    }

    void EndDrag()
    {
        if (_dragSlot < 0 || _page == null) return;
        int slotIndex = _dragSlot;
        double ticks = _dragTicks;
        _dragSlot = -1;
        if (IsMouseCaptured) ReleaseMouseCapture();
        var slot = _page.Slots.FirstOrDefault(s => s.Spec.Slot == slotIndex);
        if (slot != null) _page.SetSlotTicks(slot, (int)Math.Round(ticks));
        Redraw();
    }
}
