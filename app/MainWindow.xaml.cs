using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;
using TestingApp.ViewModels;

namespace TestingApp;

public partial class MainWindow : Window
{
    readonly MainVM _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainVM();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmChanged;
        PreviewKeyDown += (_, e) => { if (_vm.HandleKey(e)) e.Handled = true; };
        Closing += (_, _) => _vm.Shutdown();
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainVM.CurrentPage) && string.IsNullOrWhiteSpace(_vm.SearchText)) AnimatePage();
    }

    // a short fade and slide when the page changes
    void AnimatePage()
    {
        PageScroll.ScrollToTop();
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        if (PageHost.RenderTransform is TranslateTransform tt)
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
