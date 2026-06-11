using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsciiDraw.Models
{
    public class DrawDocument
    {
        public int Columns { get; set; } = 160;
        public int Rows { get; set; } = 100;
        public List<DrawElement> Elements { get; set; } = new();
        public List<GroupInfo> Groups { get; set; } = new();

        public string ToJson() =>
            JsonSerializer.Serialize(this, DrawDocumentJsonContext.Default.DrawDocument);

        public static DrawDocument FromJson(string json) =>
            JsonSerializer.Deserialize(json, DrawDocumentJsonContext.Default.DrawDocument)
                ?? new DrawDocument();
    }

    // Source-generated serializer: required for NativeAOT (no reflection), and
    // faster at runtime. Polymorphic Elements work via the [JsonDerivedType]
    // attributes on DrawElement; enums serialize as strings.
    [JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(DrawDocument))]
    internal partial class DrawDocumentJsonContext : JsonSerializerContext
    {
    }
}
