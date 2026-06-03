using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 程序化关卡生成（首版：确定性 Z 字形主路径，对应原型 PATH_* 常量）。
    /// 通过 GameApp.Random 命名流产出，保证同种子可复现。预制段 chunk 拼接为后续可替换策略。
    /// </summary>
    public static class LevelGenerator
    {
        private const string Stream = "level";

        // 主路径 x 中心循环（像素 → 单位）。
        private static readonly float[] PathXs =
        {
            GameConst.Px(280f), GameConst.Px(560f), GameConst.Px(820f), GameConst.Px(540f),
        };

        // 斜坡索引与朝向（设计文档 6.4）。
        private static readonly Dictionary<int, SlopeDir> SlopeMap = new Dictionary<int, SlopeDir>
        {
            { 5, SlopeDir.Right }, { 14, SlopeDir.Left }, { 21, SlopeDir.Right },
            { 30, SlopeDir.Left }, { 37, SlopeDir.Right }, { 46, SlopeDir.Left },
        };

        private static readonly HashSet<int> CheckpointIdx = new HashSet<int> { 7, 15, 23, 31, 39, 47 };

        public static LevelData Generate(int platformCount = 48)
        {
            if (!GameApp.Random.IsInitialized)
            {
                GameApp.Random.Init(string.Empty);
            }

            IRandomStream rng = GameApp.Random.Stream(Stream);
            var data = new LevelData { PlatformCount = platformCount };

            float bottomY = GameConst.Px(240f);
            float tileH = GameConst.Tile;

            float lastTopY = bottomY;
            for (int i = 0; i < platformCount; i++)
            {
                float cx = PathXs[i % PathXs.Length];
                float topY = bottomY + i * GameConst.VerticalStep;
                lastTopY = topY;

                bool isCp = CheckpointIdx.Contains(i);
                bool isSlope = SlopeMap.ContainsKey(i);

                if (isSlope)
                {
                    float w = GameConst.Px(64f);
                    var rect = new AABB(cx - w * 0.5f, topY, w, tileH);
                    data.Slopes.Add(new SlopeData { Rect = rect, Dir = SlopeMap[i] });
                    // 斜坡也作为固体参与 X 轴阻挡（底部矩形），Y 轴由 slopeTopY 单独吸附。
                    continue;
                }

                float width = isCp ? GameConst.Px(160f) : rng.Range(GameConst.Px(80f), GameConst.Px(128f));
                var platform = new AABB(cx - width * 0.5f, topY, width, tileH);
                data.Platforms.Add(platform);
                data.Solids.Add(platform);

                if (i == 0)
                {
                    data.StartPos = new Vector2(cx - GameConst.PlayerWidth * 0.5f, topY + tileH);
                }

                // —— 地刺：第 8 平台起，非 CP 非斜坡，每隔几个平台布置（单侧留 ≥ 40px 立足）——
                if (i >= 8 && !isCp && rng.NextBool(0.55))
                {
                    float spikeW = Mathf.Min(GameConst.Px(48f), width - GameConst.Px(40f));
                    if (spikeW >= GameConst.Px(16f))
                    {
                        bool leftAligned = rng.NextBool();
                        float sx = leftAligned ? platform.MinX : platform.MaxX - spikeW;
                        data.Spikes.Add(new AABB(sx, topY + tileH, spikeW, GameConst.Px(10f)));
                    }
                }

                // —— 怪物 spawn：蚊群(第8起) / 暗影(第12起) ——
                if (i >= (int)GameConst.MosquitoFirstPlatform && i % 4 == 0)
                {
                    data.Spawns.Add(new MonsterSpawn
                    {
                        Kind = MonsterKind.Mosquito,
                        Pos = new Vector2(cx, topY + GameConst.Px(40f)),
                    });
                }

                if (i >= (int)GameConst.ShadowFirstPlatform && i % 6 == 0)
                {
                    data.Spawns.Add(new MonsterSpawn
                    {
                        Kind = MonsterKind.Shadow,
                        Pos = new Vector2(cx + GameConst.Px(24f), topY + tileH),
                    });
                }

                if (isCp)
                {
                    data.Checkpoints.Add(new CheckpointData
                    {
                        Pos = new Vector2(cx, topY + tileH + GameConst.Px(32f)),
                        IsEndpoint = false,
                    });
                }
            }

            // —— 山顶终点段：宽平台 + 灯塔光柱 ——
            float summitY = lastTopY + GameConst.VerticalStep;
            float summitCx = PathXs[platformCount % PathXs.Length];
            float summitW = GameConst.Px(240f);
            var summit = new AABB(summitCx - summitW * 0.5f, summitY, summitW, tileH);
            data.Platforms.Add(summit);
            data.Solids.Add(summit);
            data.Checkpoints.Add(new CheckpointData
            {
                Pos = new Vector2(summitCx, summitY + tileH + GameConst.Px(40f)),
                IsEndpoint = true,
            });

            // —— 世界边界墙 ——
            float worldTop = summitY + GameConst.Px(320f);
            float wallThick = GameConst.Tile;
            data.Walls.Add(new AABB(-wallThick, GameConst.Px(-640f), wallThick, worldTop + GameConst.Px(1280f)));
            data.Walls.Add(new AABB(GameConst.WorldWidth, GameConst.Px(-640f), wallThick, worldTop + GameConst.Px(1280f)));
            data.Solids.Add(data.Walls[0]);
            data.Solids.Add(data.Walls[1]);

            data.WorldHeight = worldTop;
            data.FallDeathY = bottomY - GameConst.Px(640f);

            return data;
        }
    }
}
