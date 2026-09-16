using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

#pragma warning disable CA1416 // Harness desktop bootstrap uses Windows DPAPI; non-Windows secret providers are P2.

namespace Wesa.Harness.Api.Infrastructure.Authentication;

public sealed record IssuedCapabilityToken(string Token, string Role, string Name);
public sealed record CapabilityTokenInfo(Guid Id, string Name, string Role, string Prefix, string Status, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? ExpiresAt);

public sealed class CapabilityTokenStore(IConfiguration configuration, ILogger<CapabilityTokenStore> logger)
{
    private static readonly Guid LegacyAdminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LegacyLumioId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private sealed record StoredToken(Guid Id, string Name, string Role, string Hash, string Prefix, string Status, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? ExpiresAt);
    private sealed record StoreFile(List<StoredToken> Tokens);
    private readonly object _gate = new();
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WesaHarness", "capability-tokens.dat");

    public bool IsInitialized
    {
        get { MigrateLegacyIfNeeded(); return Read().Tokens.Any(token => token.Role == "greg" && token.Status == "active"); }
    }

    public (bool Initialized, bool LegacyConfigPresent) Status()
        => (IsInitialized, !string.IsNullOrWhiteSpace(configuration["Security:CapabilityToken"]));

    public (IssuedCapabilityToken Admin, IssuedCapabilityToken Lumio) Initialize()
    {
        lock (_gate)
        {
            if (IsInitialized) throw new InvalidOperationException("Harness is already initialized");
            if (!string.IsNullOrWhiteSpace(configuration["Security:CapabilityToken"]))
                throw new InvalidOperationException("Legacy capability token exists; migrate it before bootstrap");
            var admin = Issue("Greg admin", "greg");
            var lumio = Issue("Lumio service", "lumio-client");
            logger.LogInformation("Harness capability token store initialized with {Count} principals", 2);
            return (admin, lumio);
        }
    }

    public IReadOnlyList<CapabilityTokenInfo> List()
    {
        MigrateLegacyIfNeeded();
        return Read().Tokens.Select(value => new CapabilityTokenInfo(value.Id, value.Name, value.Role, value.Prefix, value.Status, value.CreatedAt, value.LastUsedAt, value.ExpiresAt)).ToArray();
    }

    public IssuedCapabilityToken Create(string name, string role, DateTimeOffset? expiresAt = null)
    {
        if (role is not ("greg" or "lumio-client" or "observer")) throw new ArgumentException("Unsupported token role");
        lock (_gate) return Issue(name, role, expiresAt);
    }

    public IssuedCapabilityToken Rotate(Guid id, TimeSpan overlap)
    {
        lock (_gate)
        {
            MigrateLegacyIfNeeded();
            var file = Read();
            var old = file.Tokens.SingleOrDefault(value => value.Id == id && value.Status == "active")
                ?? throw new KeyNotFoundException("Active token not found");
            var replacement = Issue(old.Name, old.Role, old.ExpiresAt);
            var index = file.Tokens.FindIndex(value => value.Id == old.Id);
            file.Tokens[index] = old with { Status = "overlap", ExpiresAt = DateTimeOffset.UtcNow.Add(overlap) };
            Write(file);
            return replacement;
        }
    }

    public void Revoke(Guid id)
    {
        lock (_gate)
        {
            var file = Read();
            var index = file.Tokens.FindIndex(value => value.Id == id && value.Status is "active" or "overlap");
            if (index < 0) throw new KeyNotFoundException("Token not found");
            file.Tokens[index] = file.Tokens[index] with { Status = "revoked", ExpiresAt = DateTimeOffset.UtcNow };
            Write(file);
        }
    }

    public (string Role, Guid Id)? Authenticate(string supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return null;
        lock (_gate)
        {
            MigrateLegacyIfNeeded();
            var file = Read();
            var hash = Hash(supplied);
            var index = file.Tokens.FindIndex(value => value.Hash == hash && value.Status is "active" or "overlap" && (!value.ExpiresAt.HasValue || value.ExpiresAt > DateTimeOffset.UtcNow));
            if (index >= 0)
            {
                file.Tokens[index] = file.Tokens[index] with { LastUsedAt = DateTimeOffset.UtcNow };
                Write(file);
                return (file.Tokens[index].Role, file.Tokens[index].Id);
            }
        }
        return null;
    }

    private void MigrateLegacyIfNeeded()
    {
        lock (_gate)
        {
            var file = Read();
            if (file.Tokens.Count > 0) return;
            var now = DateTimeOffset.UtcNow;
            var migrated = new List<StoredToken>();
            var admin = configuration["Security:CapabilityToken"];
            var lumio = configuration["Security:LumioServiceToken"];
            if (!string.IsNullOrWhiteSpace(admin)) migrated.Add(new StoredToken(LegacyAdminId, "Greg admin", "greg", Hash(admin), admin[..Math.Min(10, admin.Length)], "active", now, null, null));
            if (!string.IsNullOrWhiteSpace(lumio)) migrated.Add(new StoredToken(LegacyLumioId, "Lumio service", "lumio-client", Hash(lumio), lumio[..Math.Min(10, lumio.Length)], "active", now, null, null));
            if (migrated.Count > 0)
            {
                Write(new StoreFile(migrated));
                logger.LogInformation("Migrated {Count} legacy capability tokens into the protected store", migrated.Count);
            }
        }
    }

    private IssuedCapabilityToken Issue(string name, string role, DateTimeOffset? expiresAt = null)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var file = Read();
        file.Tokens.Add(new StoredToken(Guid.NewGuid(), name, role, Hash(token), token[..10], "active", DateTimeOffset.UtcNow, null, expiresAt));
        Write(file);
        return new IssuedCapabilityToken(token, role, name);
    }

    private StoreFile Read()
    {
        if (!File.Exists(_path)) return new StoreFile([]);
        try
        {
            var encrypted = File.ReadAllBytes(_path);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoreFile>(plain) ?? new StoreFile([]);
        }
        catch { return new StoreFile([]); }
    }

    private void Write(StoreFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(file);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        var temporary = _path + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, _path, true);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool Matches(string? expected, string supplied) => !string.IsNullOrWhiteSpace(expected) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
}

#pragma warning restore CA1416
