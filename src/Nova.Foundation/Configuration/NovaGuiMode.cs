using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeerCast.Nova.Foundation.Configuration;

[JsonConverter(typeof(NovaGuiModeJsonConverter))]
public enum NovaGuiMode
{
    Classic,
    Modern,
}

// Tolerance is confined to this optional appearance field. Malformed documents
// and errors in other settings still follow the existing store error path.
public sealed class NovaGuiModeJsonConverter : JsonConverter<NovaGuiMode>
{
    public override bool HandleNull => true;

    public override NovaGuiMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() == nameof(NovaGuiMode.Modern))
            return NovaGuiMode.Modern;
        reader.Skip();
        return NovaGuiMode.Classic;
    }

    public override void Write(Utf8JsonWriter writer, NovaGuiMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == NovaGuiMode.Modern ? nameof(NovaGuiMode.Modern) : nameof(NovaGuiMode.Classic));
}
