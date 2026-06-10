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

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        public static DrawDocument FromJson(string json) =>
            JsonSerializer.Deserialize<DrawDocument>(json, JsonOptions) ?? new DrawDocument();
    }
}
