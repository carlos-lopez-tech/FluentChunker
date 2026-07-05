using System.Text.Json;

namespace FluentChunker
{
    public class MetadataBuilder
    {
        private readonly Dictionary<string, JsonElement> _metadata = [];

        public void Set<T>(string key, T value)
        {
            _metadata[key] = JsonSerializer.SerializeToElement(value);
        }

        internal Dictionary<string, JsonElement> Build() => new(_metadata);
    }
}
