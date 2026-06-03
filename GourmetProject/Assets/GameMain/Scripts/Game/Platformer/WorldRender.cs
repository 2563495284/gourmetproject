using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 世界内 SpriteRenderer 创建助手。统一使用 URP 2D Lit Sprite 材质，
    /// 使精灵能被 Light2D 照亮（默认 Sprites/Default 不受 2D 光影响）。
    /// </summary>
    public static class WorldRender
    {
        public const string LayerTerrain = "Terrain";
        public const string LayerSpike = "Spike";
        public const string LayerCheckpoint = "Checkpoint";
        public const string LayerMonster = "Monster";
        public const string LayerPlayer = "Player";

        private static Material _litMat;

        public static Material LitMaterial
        {
            get
            {
                if (_litMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                    if (sh == null) sh = Shader.Find("Sprites/Default");
                    _litMat = new Material(sh);
                }
                return _litMat;
            }
        }

        public static SpriteRenderer Create(string name, Sprite sprite, Vector2 pos, Transform parent, int order = 0)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = LitMaterial;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>创建不受 2D 光照影响的精灵（迷雾光点、无敌环等始终可见的指示物）。</summary>
        public static SpriteRenderer CreateUnlit(string name, Sprite sprite, Vector2 pos, Transform parent, int order = 0)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>创建平铺精灵（用于宽度可变的平台/地刺）。size 为世界单位。</summary>
        public static SpriteRenderer CreateTiled(string name, Sprite sprite, AABB rect, Transform parent, int order = 0)
        {
            var sr = Create(name, sprite, rect.Center, parent, order);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(rect.Width, rect.Height);
            return sr;
        }
    }
}
