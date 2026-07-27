using System.Collections;
using System.Text.Json;

namespace FluentChunker.Contracts
{
    /// <summary>
    /// Immutable, value-equal metadata attached to a <see cref="Chunk"/>.
    /// Build entries through <see cref="FluentChunker.MetadataBuilder"/> or add to an existing instance with <see cref="With{T}"/>; there is no way to modify an instance in place.
    /// </summary>
    /// <remarks>
    /// Keys are compared ordinally (case-sensitive).
    /// Values are compared by their raw JSON text (<see cref="JsonElement.GetRawText"/>), which is stricter than semantic JSON equality: two elements that differ only in number formatting, whitespace or property order compare unequal.
    /// Entries created through <see cref="FluentChunker.MetadataBuilder.Set{T}"/> or <see cref="With{T}"/> serialize deterministically, so the distinction only surfaces for hand-crafted <see cref="JsonElement"/> values.
    /// Hash codes are computed per process and must never be persisted.
    /// </remarks>
    public sealed class ChunkMetadata : IReadOnlyDictionary<string, JsonElement>, IEquatable<ChunkMetadata>
    {
        /// <summary>
        /// The shared empty instance.
        /// It is the default of <see cref="Chunk.Metadata"/>, so chunks without metadata allocate no container at all.
        /// </summary>
        public static ChunkMetadata Empty { get; } = new(new Dictionary<string, JsonElement>(StringComparer.Ordinal));

        private readonly Dictionary<string, JsonElement> _entries;

        // 0 means "not yet computed"; computed values are coerced away from 0.
        // Racing threads compute the same value, so the unsynchronized write is benign.
        private int _hashCode;

        // Takes ownership: callers hand over an ordinal-keyed dictionary they will not touch again.
        internal ChunkMetadata(Dictionary<string, JsonElement> entries)
        {
            _entries = entries;
        }

        /// <summary>
        /// Returns a new instance with <paramref name="key"/> set to <paramref name="value"/>, serialized the same way as <see cref="FluentChunker.MetadataBuilder.Set{T}"/>.
        /// Existing entries are kept; setting an existing key replaces its value.
        /// This instance is not modified.
        /// </summary>
        /// <typeparam name="T">The value type.
        /// Must be serializable by <see cref="JsonSerializer"/>.</typeparam>
        /// <param name="key">The metadata key.
        /// Avoid <c>headingPath</c>, which structure-aware chunking reserves.</param>
        /// <param name="value">The value to store.</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public ChunkMetadata With<T>(string key, T value)
        {
            ArgumentNullException.ThrowIfNull(key);

            var entries = new Dictionary<string, JsonElement>(_entries, StringComparer.Ordinal)
            {
                [key] = JsonSerializer.SerializeToElement(value)
            };
            return new ChunkMetadata(entries);
        }

        /// <inheritdoc/>
        public JsonElement this[string key] => _entries[key];

        /// <inheritdoc/>
        public IEnumerable<string> Keys => _entries.Keys;

        /// <inheritdoc/>
        public IEnumerable<JsonElement> Values => _entries.Values;

        /// <inheritdoc/>
        public int Count => _entries.Count;

        /// <inheritdoc/>
        public bool ContainsKey(string key) => _entries.ContainsKey(key);

        /// <inheritdoc/>
        public bool TryGetValue(string key, out JsonElement value) => _entries.TryGetValue(key, out value);

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<string, JsonElement>> GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Value equality: same entry count, same keys (ordinal), and each value's raw JSON text matches ordinally.
        /// See the type remarks for what raw-text comparison implies.
        /// </summary>
        /// <param name="other">The instance to compare with.</param>
        /// <returns><see langword="true"/> when both hold the same entries.</returns>
        public bool Equals(ChunkMetadata? other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            if (_entries.Count != other._entries.Count)
                return false;

            foreach (var (key, value) in _entries)
            {
                if (!other._entries.TryGetValue(key, out var otherValue))
                    return false;
                if (!string.Equals(value.GetRawText(), otherValue.GetRawText(), StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as ChunkMetadata);

        /// <summary>
        /// Combines every entry order-independently, since dictionary enumeration order is unspecified and equal instances must agree.
        /// Computed once and cached, which immutability makes safe.
        /// </summary>
        /// <returns>The hash code, valid only within this process.</returns>
        public override int GetHashCode()
        {
            if (_hashCode != 0)
                return _hashCode;

            var hash = 0;
            foreach (var (key, value) in _entries)
            {
                unchecked
                {
                    hash += HashCode.Combine(
                        StringComparer.Ordinal.GetHashCode(key),
                        value.GetRawText().GetHashCode(StringComparison.Ordinal));
                }
            }

            _hashCode = hash == 0 ? 1 : hash;
            return _hashCode;
        }
    }
}
