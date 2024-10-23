namespace DirectoryWatcher.Test
{
    public readonly struct TestMetaInfo : IDirectoryWatcherValue
    {
        public TestMetaInfo()
        {
        }

        public string Name { get; init; } = string.Empty;
        public string FirstLine { get; init; } = string.Empty;
        public DateTime LastWriteTimeUtc { get; init; } = DateTime.MinValue;
        public int Size { get; init; } = 0;
    }

    public class TestWatcher : DirectoryWatcherBase<string, TestMetaInfo>
    {
        public TestWatcher(DirectoryInfo directory) : base(directory)
        {
        }

        protected override bool IsFileRelevant(FileInfo file)
        {
            return file.Name.EndsWith(".txt");
        }

        protected override TestMetaInfo ExtractData(FileInfo file)
        {
            return new TestMetaInfo 
            {
                Name = file.Name, 
                LastWriteTimeUtc = file.LastWriteTimeUtc, 
                FirstLine = File.ReadAllLines(file.FullName).First(),
            };
        }

        protected override string GetKey(FileInfo file)
        {
            return file.Name;
        }
    }
}
