using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TestingApp;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            try
            {
                string log = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Testing", "crash.log");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(log)!);
                File.AppendAllText(log, DateTime.Now.ToString("s") + "\n" + e.Exception + "\n\n");
            }
            catch { }
            MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message + "\n\n(details were written to %AppData%\\Testing\\crash.log)", "Testing", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
    }

    private void CommitBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();
        e.Handled = true;
    }
}
