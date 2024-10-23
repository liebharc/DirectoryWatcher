namespace DirectoryWatcher.Test
{
    [TestClass]
    public class DirectoryWatcherTest
    {
        private DirectoryInfo _dir;

        [TestInitialize]
        public void Init()
        {
            _dir = CreateTempDir();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _dir.Delete(true);
        }

        [TestMethod]
        public void TestMonitorDir()
        {
            using var monitor = new TestWatcher(_dir);
            Assert.AreEqual(monitor.Keys.Count, 0);

            Touch(_dir, "file1.txt");
            Assert.AreEqual(monitor.Keys.Count, 1);

            Touch(_dir, "file2.txt");
            Assert.AreEqual(monitor.Keys.Count, 2);

            Delete(_dir, "file1.txt");
            Assert.AreEqual(monitor.Keys.Count, 1);

            Move(_dir, "file2.txt", "file.ignore");
            Assert.AreEqual(monitor.Keys.Count, 0);

            Move(_dir, "file.ignore", "file1.txt");
            Assert.AreEqual(monitor.Keys.Count, 1);
            Assert.AreEqual(monitor["file1.txt"].FirstLine, "TEST");
        }

        [TestMethod]
        public void TestReloadMonitor()
        {
            InitMonitorWithSomeFiles();
            using (var monitor = new TestWatcher(_dir))
            {
                Assert.AreEqual(monitor.Keys.Count, 10);

                // Delete the index to force that it's rebuild
                Directory.Delete(Path.Combine(monitor.Directory.FullName, ".watcherindex"), true);
            }

            using (var monitor = new TestWatcher(_dir))
            {
                Assert.AreEqual(monitor.Keys.Count, 10);
            }
        }

        [TestMethod]
        public void TestDamagedIndex()
        {
            InitMonitorWithSomeFiles();

            File.Delete(Path.Combine(_dir.FullName, "File0.txt"));
            File.Delete(Path.Combine(_dir.FullName, ".watcherindex", "File1.txt"));
            File.WriteAllText(Path.Combine(_dir.FullName, "File2.txt"), "CHANGED");

            using (var monitor = new TestWatcher(_dir))
            {
                Assert.AreEqual(monitor.Keys.Count, 9);
                Assert.AreEqual(monitor["File2.txt"].FirstLine, "CHANGED");
            }
        }

        private void InitMonitorWithSomeFiles()
        {
            using var monitor = new TestWatcher(_dir);
            for (int i = 0; i < 10; i++)
            {
                Touch(_dir, "File" + i + ".txt");
            }
        }

        private DirectoryInfo CreateTempDir()
        {
            var dirname = Path.GetTempFileName();
            File.Delete(dirname);
            var dir = new DirectoryInfo(dirname);
            dir.Create();
            return dir;
        }

        private void Touch(DirectoryInfo dir, string filename)
        {
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "TEST\nHello World");
            File.Move(tempFile, Path.Combine(dir.FullName, filename), true);
            AllowEventsToTrigger();
        }

        private void Delete(DirectoryInfo dir, string filename)
        {
            File.Delete(Path.Combine(dir.FullName, filename));
            AllowEventsToTrigger();
        }

        private void Move(DirectoryInfo dir, string oldName, string newName)
        {
            File.Move(Path.Combine(dir.FullName, oldName), Path.Combine(dir.FullName, newName));
            AllowEventsToTrigger();
        }

        private void AllowEventsToTrigger()
        {
            Thread.Sleep(1);
        }
    }
}