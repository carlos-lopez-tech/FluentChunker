using Microsoft.ML.Tokenizers;

namespace FluentChunker
{
    public sealed class MicrosoftMLTokenizerAdapter : ITokenizer
    {
        private readonly Tokenizer _tokenizer;

        public MicrosoftMLTokenizerAdapter(string encodingName)
        {
            _tokenizer = TiktokenTokenizer.CreateForEncoding(encodingName);
        }

        public int CountTokens(string text) => _tokenizer.CountTokens(text);

        public string Decode(IReadOnlyList<int> tokens) => _tokenizer.Decode(tokens);

        public IReadOnlyList<int> Encode(string text) => _tokenizer.EncodeToIds(text);
    }
}
