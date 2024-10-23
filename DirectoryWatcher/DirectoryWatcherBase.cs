using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;

namespace DirectoryWatcher 
{ 
    public abstract class DirectoryWatcherBase<TKey, TValue> : IDisposable
        where TKey: notnull
    {
        private readonly string IndexerDirName = ".watcherindex";

        private readonly FileSystemWatcher _fileSystemWatcher;

        private readonly IDictionary<TKey, TValue> _cache = new Dictionary<TKey, TValue>();

        private readonly string _extension;

        private bool disposedValue;

        public DirectoryInfo Directory { get; }

        private DirectoryInfo Index { get; }

        protected DirectoryWatcherBase(DirectoryInfo directory, string extension)
        {
            Directory = directory;
            _extension = extension;
            if (extension.StartsWith("."))
            {
                throw new ArgumentException("Must not start with a .", nameof(extension));
            }
            Index = new DirectoryInfo(Path.Combine(directory.FullName, IndexerDirName));
            Init();

            _fileSystemWatcher = new FileSystemWatcher(directory.FullName);
            _fileSystemWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size;
            _fileSystemWatcher.Changed += OnChanged;
            _fileSystemWatcher.Created += OnCreated;
            _fileSystemWatcher.Deleted += OnDeleted;
            _fileSystemWatcher.Renamed += OnRenamed;

            _fileSystemWatcher.Filter = "*." + extension;
            _fileSystemWatcher.IncludeSubdirectories = false;
            _fileSystemWatcher.EnableRaisingEvents = true;
        }

        private void Init()
        {
            var cachedKeys = new ConcurrentBag<TKey>();
            if (Index.Exists)
            {
                var indexFiles = Index.GetFiles();
                foreach (var file in indexFiles)
                {
                    var cachedValue = DeserializeFromIndexFile(file);
                    var dataFile = Path.Combine(Directory.FullName, file.Name);
                    if (File.GetLastWriteTimeUtc(file.FullName) == File.GetLastWriteTimeUtc(dataFile))
                    {
                        var key = GetKey(file);
                        _cache[key] = cachedValue;
                        cachedKeys.Add(key);
                    }
                    else
                    {
                        file.Delete();
                    }
                }
            }
            else
            {
                Index.Create();
            }

            var existingKeys = new ConcurrentBag<TKey>();
            var fileInfos = Directory.GetFiles();
            fileInfos.AsParallel().ForAll(
                fileInfo =>
                {
                    if (!IsFileRelevantInternal(fileInfo))
                    {
                        return;
                    }

                    var key = GetKey(fileInfo);
                    existingKeys.Add(key);

                    if (cachedKeys.Contains(key))
                    {
                        return;
                    }

                    AddFile(fileInfo);
                }
            );

            var obsoleteKeys = new HashSet<TKey>();
            foreach (var key in _cache.Keys)
            {
                if (!existingKeys.Contains(key))
                {
                    obsoleteKeys.Add(key);
                }
            }

            if (obsoleteKeys.Any())
            {
                var indexFiles = Index.GetFiles();
                foreach (var file in indexFiles)
                {
                    var key = GetKey(file);
                    if (obsoleteKeys.Contains(key))
                    {
                        DeleteKey(file, key);
                    }
                }
            }
        }

        public IList<TKey> Keys 
        {  
            get
            {
                var result = new List<TKey>();
                lock (_cache)
                {
                    result.AddRange(_cache.Keys);
                }

                return result;
            }
        }

        public IList<TValue> Values
        {
            get
            {
                var result = new List<TValue>();
                lock (_cache)
                {
                    result.AddRange(_cache.Values);
                }

                return result;
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                lock (_cache)
                {
                    return _cache[key];
                }
            }
        }

        protected virtual bool IsFileRelevant(FileInfo file)
        {
            return file.Name.EndsWith("." + _extension);
        }

        private bool IsFileRelevantInternal(FileInfo file)
        {
            return file.Name != IndexerDirName && IsFileRelevant(file);
        }

        protected abstract TKey GetKey(FileInfo file);

        protected abstract TValue ExtractData(FileInfo file);

        private void SerializeUsingTempFile(FileInfo file, TValue value)
        {
            var tempFile = Path.GetTempFileName();
            SerializeToIndexFile(new FileInfo(tempFile), value);
            File.Move(tempFile, file.FullName, true);
        }

        protected virtual void SerializeToIndexFile(FileInfo file, TValue value)
        {
            var serialized = JsonSerializer.Serialize(value);
            File.WriteAllText(file.FullName, serialized);
        }

        protected virtual TValue DeserializeFromIndexFile(FileInfo file)
        {
            return JsonSerializer.Deserialize<TValue>(File.ReadAllText(file.FullName));
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed)
            {
                return;
            }

            var file = new FileInfo(e.FullPath);
            if (!IsFileRelevantInternal(file))
            {
                return;
            }

            AddFile(file);
        }

       private void OnCreated(object sender, FileSystemEventArgs e)
       {
            var file = new FileInfo(e.FullPath);
            if (!IsFileRelevant(file))
            {
                return;
            }

            AddFile(file);
        }

        private void AddFile(FileInfo file)
        {
            var update = ExtractData(file);
            var key = GetKey(file);
            lock (_cache)
            {
                if (_cache.ContainsKey(key))
                {
                    _cache.Remove(key);
                }
                _cache.Add(key, update);
            }

            var indexFile = new FileInfo(Path.Combine(Index.FullName, file.Name));
            SerializeUsingTempFile(indexFile, update);
            File.SetLastWriteTimeUtc(indexFile.FullName, file.LastWriteTimeUtc);
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            var file = new FileInfo(e.FullPath);
            if (!IsFileRelevantInternal(file))
            {
                return;
            }
            var key = GetKey(file);
            DeleteKey(file, key);
        }

        private void DeleteKey(FileInfo file, TKey key)
        {
            lock (_cache)
            {
                _cache.Remove(key);
            }

            var indexFile = new FileInfo(Path.Combine(Index.FullName, file.Name));
            indexFile.Delete();
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            var oldFile = new FileInfo(e.OldFullPath);
            var oldKey = GetKey(oldFile);
            var newFile = new FileInfo(e.FullPath);
            var newKey = GetKey(newFile);
            bool wasRelevant = oldFile.Directory.FullName == Directory.FullName && IsFileRelevantInternal(oldFile);
            bool isRelevantNow = newFile.Directory.FullName == Directory.FullName && IsFileRelevantInternal(newFile);
            
            if (!wasRelevant && isRelevantNow)
            {
                AddFile(newFile);
            }
            else if (wasRelevant && !isRelevantNow)
            {
                DeleteKey(oldFile, oldKey);
            }
            else if (wasRelevant && isRelevantNow)
            {
                lock (_cache)
                {
                    _cache.Add(newKey, _cache[oldKey]);
                    _cache.Remove(oldKey);
                }

                var indexFile = new FileInfo(Path.Combine(Index.FullName, oldFile.Name));
                indexFile.MoveTo(Path.Combine(Path.Combine(Index.FullName, newFile.Name)));
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _fileSystemWatcher.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}