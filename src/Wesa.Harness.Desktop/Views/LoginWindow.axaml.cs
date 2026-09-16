using Avalonia.Controls;

namespace Wesa.Harness.Desktop.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private void CloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
        => Close();

    private void MinimizeWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
        => WindowState = WindowState.Minimized;
}
