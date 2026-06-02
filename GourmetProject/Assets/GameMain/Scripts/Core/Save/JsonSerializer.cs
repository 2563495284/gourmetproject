using System.Text;
using Newtonsoft.Json;

namespace GourmetProject.Core.Save
{
    /// <summary>
    /// 基于 Newtonsoft.Json 的序列化实现。开发期默认使用，存档为可读 JSON，便于排查与 diff。
    /// </summary>
    public sealed class JsonSerializer : ISerializer
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly JsonSerializerSettings _settings;

        public JsonSerializer(bool indented = true)
        {
            _settings = new JsonSerializerSettings
            {
                Formatting = indented ? Formatting.Indented : Formatting.None,
                NullValueHandling = NullValueHandling.Include,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                // 保留具体类型信息有助于多态存档；如不需要可去掉。
                TypeNameHandling = TypeNameHandling.Auto,
            };
        }

        public string FileExtension => "json";

        public byte[] Serialize<T>(T value)
        {
            string json = JsonConvert.SerializeObject(value, _settings);
            return Utf8NoBom.GetBytes(json);
        }

        public T Deserialize<T>(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return default;
            }

            string json = Utf8NoBom.GetString(data);
            return JsonConvert.DeserializeObject<T>(json, _settings);
        }
    }
}
