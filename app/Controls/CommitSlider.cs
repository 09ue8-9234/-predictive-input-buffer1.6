using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TestingApp.Controls;

// A slider that only reports changes the user makes with the mouse. The value itself is bound one way (from the setting),
// so WPF's own range coercion while the slider is being set up can never be written back into a setting by accident
// (a slider created before its Maximum is known would otherwise "correct" e.g. 240 to 10 and save it).
public sealed class CommitSlider : Slider
{
    public static readonly DependencyProperty CommitCommandProperty =
        DependencyProperty.Register(nameof(CommitCommand), typeof(ICommand), typeof(CommitSlider));

    public ICommand? CommitCommand
    {
        get => (ICommand?)GetValue(CommitCommandProperty);
        set => SetValue(CommitCommandProperty, value);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if ((IsMouseOver || IsMouseCaptureWithin) && CommitCommand is { } cmd && cmd.CanExecute(newValue)) cmd.Execute(newValue);
    }
}
