using System;
using System.Collections.Generic;
using System.IO;
using GourmetProject.Core.Diagnostics;
using GourmetProject.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>
    /// 存档系统的往返测试：数据无损往返、二次写产生备份、校验和能识别篡改、版本迁移生效。
    /// </summary>
    public class SaveServiceTests
    {
        private string _dir;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gp_save_tests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void Teardown()
        {
            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, true);
                }
            }
            catch
            {
                // 清理失败不影响测试结论。
            }
        }

        public class Profile
        {
            public int Score;
            public string Name;
        }

        [Test]
        public void SaveLoad_Roundtrip_PreservesData()
        {
            var svc = new JsonSaveService(_dir);
            svc.Save("slot0", new Profile { Score = 777, Name = "Alice" });

            Assert.IsTrue(svc.Has("slot0"));
            Assert.IsTrue(svc.TryLoad<Profile>("slot0", out Profile loaded));
            Assert.AreEqual(777, loaded.Score);
            Assert.AreEqual("Alice", loaded.Name);
        }

        [Test]
        public void Save_Twice_CreatesBackup_AndLoadsLatest()
        {
            var svc = new JsonSaveService(_dir);
            svc.Save("slot", new Profile { Score = 1 });
            svc.Save("slot", new Profile { Score = 2 });

            string backup = Path.Combine(_dir, "slot.sav.bak");
            Assert.IsTrue(File.Exists(backup), "second save should produce a .bak backup");

            Assert.IsTrue(svc.TryLoad<Profile>("slot", out Profile loaded));
            Assert.AreEqual(2, loaded.Score);
        }

        [Test]
        public void TamperedData_FailsChecksum_AndRefusesToLoad()
        {
            var svc = new JsonSaveService(_dir); // 默认开启校验和
            svc.Save("slot", new Profile { Score = 100, Name = "Bob" });

            string path = Path.Combine(_dir, "slot.sav");
            string json = File.ReadAllText(path);
            // 篡改 data 内的字符串值，但不修正校验和（"Bob" 不会出现在小写十六进制校验和中）。
            File.WriteAllText(path, json.Replace("Bob", "Haxed"));

            var sink = new CaptureLogSink();
            Log.SetSink(sink);
            try
            {
                Assert.IsFalse(svc.TryLoad<Profile>("slot", out _), "tampered save must fail checksum verification");
            }
            finally
            {
                Log.SetSink(null);
            }

            Assert.IsTrue(sink.Contains(LogLevel.Error, "Save", "failed checksum"), "checksum failure should be logged");
        }

        [Test]
        public void Migration_UpgradesOldVersionData()
        {
            var v1 = new JsonSaveService(_dir, new SaveServiceOptions { CurrentVersion = 1 });
            v1.Save("slot", new JObject { ["Score"] = 100 });

            var v2 = new JsonSaveService(
                _dir,
                new SaveServiceOptions { CurrentVersion = 2 },
                new ISaveMigration[] { new AddNameMigration() });

            Assert.IsTrue(v2.TryLoad<Profile>("slot", out Profile loaded));
            Assert.AreEqual(100, loaded.Score);
            Assert.AreEqual("Migrated", loaded.Name);
        }

        private sealed class AddNameMigration : ISaveMigration
        {
            public int FromVersion => 1;

            public JToken Migrate(JToken data)
            {
                var obj = (JObject)data;
                obj["Name"] = "Migrated";
                return obj;
            }
        }

        private sealed class CaptureLogSink : ILogSink
        {
            private readonly List<Entry> _entries = new List<Entry>();

            public void Log(LogLevel level, string tag, string message)
            {
                _entries.Add(new Entry(level, tag, message));
            }

            public bool Contains(LogLevel level, string tag, string messagePart)
            {
                foreach (Entry entry in _entries)
                {
                    if (entry.Level == level
                        && entry.Tag == tag
                        && entry.Message != null
                        && entry.Message.Contains(messagePart))
                    {
                        return true;
                    }
                }

                return false;
            }

            private readonly struct Entry
            {
                public Entry(LogLevel level, string tag, string message)
                {
                    Level = level;
                    Tag = tag;
                    Message = message;
                }

                public LogLevel Level { get; }

                public string Tag { get; }

                public string Message { get; }
            }
        }
    }
}
