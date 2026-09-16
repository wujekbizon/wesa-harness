using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wesa.Harness.Desktop.Services;

namespace Wesa.Harness.Desktop.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    public LoginViewModel(string? initialToken = null)
    {
        CapabilityToken = initialToken ?? DesktopTokenStore.ReadAdminToken() ?? string.Empty;
    }

    public event Action<string>? Authenticated;

    [ObservableProperty]
    public partial string ApiAddress { get; set; } =
        Environment.GetEnvironmentVariable("WESA_HARNESS_API_URL") ?? "http://127.0.0.1:5080";

    [ObservableProperty]
    public partial string Username { get; set; } = "Greg";

    [ObservableProperty]
    public partial string CapabilityToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private async Task Login(CancellationToken cancellationToken)
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(CapabilityToken))
        {
            ErrorMessage = "Operator and admin capability token are required.";
            return;
        }

        IsBusy = true;
        try
        {
            using var client = new HarnessApiClient(new Uri(ApiAddress), CapabilityToken.Trim());
            await client.ListPlugins(cancellationToken);
            DesktopTokenStore.SaveAdminToken(CapabilityToken.Trim());
            Authenticated?.Invoke(CapabilityToken.Trim());
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Login failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InitializeLocal(CancellationToken cancellationToken)
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            using var client = new HarnessApiClient(new Uri(ApiAddress), string.Empty);
            var status = await client.GetBootstrapStatus(cancellationToken);
            if (status.Initialized)
            {
                ErrorMessage = "Harness is already initialized. Paste the admin token.";
                return;
            }

            var issued = await client.InitializeBootstrap(cancellationToken);
            CapabilityToken = issued.AdminToken;
            DesktopTokenStore.SaveAdminToken(CapabilityToken);
            Authenticated?.Invoke(CapabilityToken);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Initialization failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
