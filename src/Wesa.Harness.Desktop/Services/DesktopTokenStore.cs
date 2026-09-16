using System.Security.Cryptography;
using System.Text;

#pragma warning disable CA1416 // Desktop token persistence uses Windows DPAPI; non-Windows provider is a later target.

namespace Wesa.Harness.Desktop.Services;

public static class DesktopTokenStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WesaHarness",
        "desktop-admin-token.dat");

    public static string? ReadAdminToken()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            var value = ProtectedData.Unprotect(File.ReadAllBytes(Path), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(value);
        }
        catch { return null; }
    }

    public static void SaveAdminToken(string token)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path, encrypted);
    }
}

#pragma warning restore CA1416
