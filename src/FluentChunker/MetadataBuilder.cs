using System.Text.Json;

namespace FluentChunker
{
    /// <summary>
    /// Collects the metadata entries stamped onto chunks.
    /// </summary>
    public class MetadataBuilder
    {
        private readonly Dictionary<string, JsonElement> _metadata = new(StringComparer.Ordinal);

        /// <summary>
        /// Adds an entry, serialized to JSON so it can be read back from <see cref="Contracts.Chunk.Metadata"/>.
        /// Setting the same key twice keeps the last value.
        /// </summary>
        /// <typeparam name="T">The value type.
        /// Must be serializable by <see cref="JsonSerializer"/>.</typeparam>
        /// <param name="key">The metadata key.
        /// Avoid <c>headingPath</c>, which structure-aware chunking reserves.</param>
        /// <param name="value">The value to store.</param>
        public void Set<T>(string key, T value)
        {
            _metadata[key] = JsonSerializer.SerializeToElement(value);
        }

        internal Dictionary<string, JsonElement> Build() => new(_metadata, StringComparer.Ordinal);
    }
}
