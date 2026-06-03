using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 把 LevelData 实例化为可见 GameObject（平台/墙/斜坡/地刺）。碰撞由玩家自定义 AABB 处理，
    /// 这里只负责渲染。检查点与怪物由各自系统负责。
    /// </summary>
    public static class LevelBuilder
    {
        public static void Build(LevelData data, Transform parent)
        {
            Sprite platform = Art.Load(Art.PlatformTile);
            Sprite wall = Art.Load(Art.WallTile);
            Sprite spike = Art.Load(Art.Spikes);
            Sprite slopeL = Art.Load(Art.SlopeLeft);
            Sprite slopeR = Art.Load(Art.SlopeRight);

            var root = new GameObject("Terrain").transform;
            root.SetParent(parent, false);

            foreach (AABB p in data.Platforms)
            {
                WorldRender.CreateTiled("Platform", platform, p, root, 0);
            }

            foreach (AABB w in data.Walls)
            {
                WorldRender.CreateTiled("Wall", wall, w, root, 0);
            }

            foreach (SlopeData sl in data.Slopes)
            {
                Sprite s = sl.Dir == SlopeDir.Right ? slopeR : slopeL;
                var sr = WorldRender.Create("Slope", s, sl.Rect.Center, root, 0);
                // 斜坡精灵 64×16 = 4×1 单位，与 rect 尺寸一致。
                if (s != null && s.bounds.size.x > 0f)
                {
                    sr.transform.localScale = new Vector3(
                        sl.Rect.Width / s.bounds.size.x,
                        sl.Rect.Height / s.bounds.size.y,
                        1f);
                }
            }

            foreach (AABB sp in data.Spikes)
            {
                WorldRender.CreateTiled("Spike", spike, sp, root, 1);
            }
        }
    }
}
