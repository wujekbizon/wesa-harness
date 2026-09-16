using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wesa.Harness.Api.Infrastructure.Rag;

public sealed record VertexRagConfiguration(
    string Project,
    string Location,
    IReadOnlyDictionary<string, string> Corpora,
    IReadOnlyDictionary<string, string[]> AllowedChildLibraries);

public sealed class VertexRagConfigurationStore(IHostEnvironment environment, IConfiguration configuration)
{
    private static readonly Regex ProjectPattern = new("^[a-z][a-z0-9-]{4,62}$", RegexOptions.Compiled);
    private readonly string _path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../../engine/state/vertex-rag-config.json"));
    private readonly object _gate = new();

    public VertexRagConfiguration Read()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
            {
                var value = JsonSerializer.Deserialize<VertexRagConfiguration>(
                    File.ReadAllText(_path),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (value is not null)
                {
                    return new VertexRagConfiguration(
                        value.Project ?? "",
                        value.Location ?? "us-central1",
                        value.Corpora ?? new Dictionary<string, string>(StringComparer.Ordinal),
                        value.AllowedChildLibraries ?? new Dictionary<string, string[]>(StringComparer.Ordinal));
                }
            }
            return new VertexRagConfiguration(
                configuration["WESA_VERTEX_RAG_PROJECT"] ?? "",
                configuration["WESA_VERTEX_RAG_LOCATION"] ?? "us-central1",
                ParseMap(configuration["WESA_VERTEX_RAG_CORPORA"]),
                ParseListMap(configuration["Lumio:AllowedChildLibraries"]));
        }
    }

    public VertexRagConfiguration Save(VertexRagConfiguration value)
    {
        Validate(value);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            File.Move(temporary, _path, true);
            return value;
        }
    }

    public bool IsLibraryAllowed(string childId, string libraryId)
        => Read().AllowedChildLibraries.TryGetValue(childId, out var libraries)
           && libraries.Contains(libraryId, StringComparer.Ordinal);

    private static void Validate(VertexRagConfiguration value)
    {
        if (!ProjectPattern.IsMatch(value.Project)) throw new ArgumentException("Invalid Vertex project ID");
        if (string.IsNullOrWhiteSpace(value.Location)) throw new ArgumentException("Vertex location is required");
        var prefix = $"projects/{value.Project}/locations/{value.Location}/ragCorpora/";
        if (value.Corpora.Count == 0 || value.Corpora.Any(item => string.IsNullOrWhiteSpace(item.Key) || !item.Value.StartsWith(prefix, StringComparison.Ordinal)))
            throw new ArgumentException("Every corpus must be an approved resource in the configured project/location");
        if (value.AllowedChildLibraries.Any(item => item.Value.Any(library => !value.Corpora.ContainsKey(library))))
            throw new ArgumentException("Child library scope references an unknown corpus mapping");
    }

    private static Dictionary<string, string> ParseMap(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? new(StringComparer.Ordinal) : JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? new(StringComparer.Ordinal);

    private static Dictionary<string, string[]> ParseListMap(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? new(StringComparer.Ordinal) : JsonSerializer.Deserialize<Dictionary<string, string[]>>(raw) ?? new(StringComparer.Ordinal);
}
