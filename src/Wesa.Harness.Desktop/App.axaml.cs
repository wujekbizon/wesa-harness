using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Wesa.Harness.Desktop.ViewModels;
using Wesa.Harness.Desktop.Views;

namespace Wesa.Harness.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<App>(optional: true)
                .Build();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(configuration["Security:CapabilityToken"] ?? string.Empty),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
