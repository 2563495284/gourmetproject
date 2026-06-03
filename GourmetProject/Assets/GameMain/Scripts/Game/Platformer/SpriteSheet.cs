using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 运行时从横向单行精灵表切帧（Sprite.Create），无需修改资源导入设置或手工 Grid 切图。
    /// 帧尺寸来自资源清单文档第二节，PPU=16，轴心居中。
    /// </summary>
    public static class SpriteSheet
    {
        private static readonly Dictionary<string, Sprite[]> Cache = new Dictionary<string, Sprite[]>();

        public static Sprite[] LoadFrames(string resourcePath, int frameW, int frameH)
        {
            if (Cache.TryGetValue(resourcePath, out Sprite[] cached)) return cached;

            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex == null)
            {
                Debug.LogWarning($"[SpriteSheet] 未找到纹理: {resourcePath}");
                Cache[resourcePath] = System.Array.Empty<Sprite>();
                return Cache[resourcePath];
            }

            int count = Mathf.Max(1, tex.width / frameW);
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                var rect = new Rect(i * frameW, 0f, frameW, frameH);
                frames[i] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect);
            }

            Cache[resourcePath] = frames;
            return frames;
        }
    }
}
