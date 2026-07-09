using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GourmetProject.Core.Diagnostics;
using GourmetProject.Core.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GourmetProject.Core.Save
{
    /// <summary>
    /// 基于文件 + JSON 的存档服务。落盘结构为一个信封：
    /// { "version": n, "checksum": "...", "data": { ... } }
    /// 提供：多槽位、版本迁移链、完整性校验、原子写（临时文件 + 替换）、上一份自动备份。
    /// 引擎无关：通过构造函数注入基目录（Runtime 传 Application.persistentDataPath）。
    /// </summary>
    public sealed class JsonSaveService : ISaveService
    {
        private const string Tag = "Save";

        private readonly string _baseDirectory;
        private readonly SaveServiceOptions _options;
        private readonly JsonSerializerSettings _settings;
        private readonly Dictionary<int, ISaveMigration> _migrations = new Dictionary<int, ISaveMigration>();

        public JsonSaveService(string baseDirectory, SaveServiceOptions options = null, IEnumerable<ISaveMigration> migrations = null)
        {
            if (string.IsNullOrEmpty(baseDirectory))
            {
                throw new ArgumentException("baseDirectory must not be empty.", nameof(baseDirectory));
            }

            _baseDirectory = baseDirectory;
            _options = options ?? new SaveServiceOptions();
            _settings = new JsonSerializerSettings
            {
                Formatting = _options.Indented ? Formatting.Indented : Formatting.None,
                NullValueHandling = NullValueHandling.Include,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto,
            };

            Directory.CreateDirectory(_baseDirectory);

            if (migrations != null)
            {
                foreach (ISaveMigration migration in migrations)
                {
                    RegisterMigration(migration);
                }
            }
        }

        public void RegisterMigration(ISaveMigration migration)
        {
            if (migration == null)
            {
                return;
            }

            _migrations[migration.FromVersion] = migration;
        }

        public void Save<T>(string slot, T data)
        {
            string path = PathOf(slot);

            JToken dataToken = data == null ? JValue.CreateNull() : JToken.FromObject(data, Newtonsoft.Json.JsonSerializer.Create(_settings));
            // 校验和基于 data 区块的紧凑序列化，避免缩进影响。
            string canonicalData = dataToken.ToString(Formatting.None);

            var envelope = new JObject
            {
                ["version"] = _options.CurrentVersion,
                ["data"] = dataToken,
            };

            if (_options.EnableChecksum)
            {
                byte[] dataBytes = Encoding.UTF8.GetBytes(canonicalData);
                envelope["checksum"] = Checksum.ComputeHex(dataBytes, _options.ChecksumSalt);
            }

            string json = envelope.ToString(_settings.Formatting);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            AtomicWrite(path, bytes);
        }

        public bool TryLoad<T>(string slot, out T data)
        {
            data = default;
            string path = PathOf(slot);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                JObject envelope = JObject.Parse(json);

                int version = envelope.Value<int?>("version") ?? _options.CurrentVersion;
                JToken dataToken = envelope["data"] ?? JValue.CreateNull();

                if (_options.EnableChecksum)
                {
                    string stored = envelope.Value<string>("checksum");
                    string canonicalData = dataToken.ToString(Formatting.None);
                    string actual = Checksum.ComputeHex(Encoding.UTF8.GetBytes(canonicalData), _options.ChecksumSalt);
                    if (!string.Equals(stored, actual, StringComparison.Ordinal))
                    {
                        Log.Error($"Save slot '{slot}' failed checksum (stored={stored}, actual={actual}). Treated as corrupted.", Tag);
                        return false;
                    }
                }

                dataToken = ApplyMigrations(slot, dataToken, version);

                data = dataToken.ToObject<T>(Newtonsoft.Json.JsonSerializer.Create(_settings));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load save slot '{slot}': {ex.Message}", Tag);
                return false;
            }
        }

        public bool Has(string slot)
        {
            return File.Exists(PathOf(slot));
        }

        public void Delete(string slot)
        {
            string path = PathOf(slot);
            SafeDelete(path);
            SafeDelete(path + ".bak");
            SafeDelete(path + ".tmp");
        }

        public IEnumerable<string> ListSlots()
        {
            var result = new List<string>();
            if (!Directory.Exists(_baseDirectory))
            {
                return result;
            }

            string pattern = "*." + _options.FileExtension;
            foreach (string file in Directory.GetFiles(_baseDirectory, pattern))
            {
                result.Add(Path.GetFileNameWithoutExtension(file));
            }

            return result;
        }

        private JToken ApplyMigrations(string slot, JToken dataToken, int fromVersion)
        {
            int version = fromVersion;
            while (version < _options.CurrentVersion)
            {
                if (!_migrations.TryGetValue(version, out ISaveMigration migration))
                {
                    Log.Warning($"Save slot '{slot}' is version {version} but no migration to {version + 1} is registered; loading as-is.", Tag);
                    break;
                }

                dataToken = migration.Migrate(dataToken) ?? dataToken;
                version++;
            }

            return dataToken;
        }

        private string PathOf(string slot)
        {
            if (string.IsNullOrEmpty(slot))
            {
                throw new ArgumentException("slot must not be empty.", nameof(slot));
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                if (slot.IndexOf(c) >= 0)
                {
                    throw new ArgumentException($"slot '{slot}' contains invalid file name characters.", nameof(slot));
                }
            }

            return Path.Combine(_baseDirectory, slot + "." + _options.FileExtension);
        }

        private void AtomicWrite(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";

            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                string backup = _options.KeepBackup ? path + ".bak" : null;
                try
                {
                    File.Replace(tmp, path, backup);
                }
                catch (Exception)
                {
                    // 某些文件系统不支持 File.Replace，退化为删除 + 移动（仍优于原地覆盖）。
                    if (backup != null)
                    {
                        SafeDelete(backup);
                        try { File.Copy(path, backup, true); } catch { /* 备份失败不致命 */ }
                    }

                    SafeDelete(path);
                    File.Move(tmp, path);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // 忽略删除失败。
            }
        }
    }
}
