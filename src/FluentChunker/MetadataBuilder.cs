namespace FluentChunker
{
    public class MetadataBuilder
    {
        private readonly Dictionary<string, string> _metadata = [];

        public void Set(string key, string value)
        {
            _metadata[key] = value;
        }

        internal Dictionary<string, string> Build() => new(_metadata);
    }
}
