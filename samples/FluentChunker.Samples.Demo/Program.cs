namespace FluentChunker.Samples.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var chunks = Chunker.Create()
                .Chunk("Hello\nWorld\nFrom FluentChunker");

            foreach (var c in chunks)
            {
                Console.WriteLine(c);
            }

            Console.ReadKey();
        }
    }
}
