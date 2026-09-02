using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Wesa.Harness.Api.Infrastructure.Security;

public static class CanonicalJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] Serialize(object value)
    {
        var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        WriteElement(writer, element);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteElement(writer, property.Value);
            }
            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
            {
                WriteElement(writer, item);
            }
            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }
}

