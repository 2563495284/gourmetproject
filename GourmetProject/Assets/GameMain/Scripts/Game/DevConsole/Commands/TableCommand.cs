using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>把当前对局的永久餐桌补齐为指定边长的正方形。</summary>
    public sealed class TableCommand : ConsoleCommand
    {
        private const string SingleCellFragmentId = "frag_1x1";

        public override string CmdName => "table";

        public override string Args => "<size:int>";

        public override string Description => "将餐桌格扩展并补齐为 size×size（不缩小餐桌）。";

        public override CmdResult Execute(string[] args)
        {
            if (!GameRunContext.HasRun)
            {
                return CmdResult.Fail("当前没有进行中的对局。");
            }

            if (args.Length != 1)
            {
                return CmdResult.Fail("用法：table <size>");
            }

            if (!int.TryParse(args[0], out int size) || size <= 0)
            {
                return CmdResult.Fail("size 必须是正整数。");
            }

            GameRun run = GameRunContext.Current;
            cfg.Character character = run.Tables?.TbCharacter.GetOrDefault(run.CharacterId);
            if (character == null)
            {
                return CmdResult.Fail($"找不到当前经营方向 '{run.CharacterId}' 的餐桌配置。");
            }

            int maxSize = Math.Min(character.MaxDiningTableWidth, character.MaxDiningTableHeight);
            if (size > maxSize)
            {
                return CmdResult.Fail($"size 不能超过当前经营方向的正方形餐桌上限 {maxSize}×{maxSize}。");
            }

            TableFragmentDef singleCellFragment = run.GetTableFragmentDef(SingleCellFragmentId);
            List<GridPos> fragmentCells = singleCellFragment != null
                ? TableFragmentBuilder.FilledCells(singleCellFragment)
                : null;
            if (fragmentCells == null || fragmentCells.Count != 1)
            {
                return CmdResult.Fail($"餐桌配置缺少单格碎片 '{SingleCellFragmentId}'。");
            }

            DiningTable table = run.BuildTablePreviewFromFragments();
            if (!TryBuildExpansionPlan(
                    table,
                    size,
                    fragmentCells[0],
                    out List<GridPos> placementOrigins,
                    out string error))
            {
                return CmdResult.Fail(error);
            }

            foreach (GridPos origin in placementOrigins)
            {
                if (!run.AddFragmentPlacement(SingleCellFragmentId, 0, origin))
                {
                    return CmdResult.Fail($"扩展餐桌失败：无法在 {origin} 添加单格碎片。");
                }
            }

            run.RequestSave();
            BattleForm.Active?.RefreshPersistentHud();

            string suffix = BattleForm.Active?.CurrentView == GameplayView.Food
                ? "；当前营业已生成的餐桌保持不变，后续营业生效"
                : string.Empty;
            return CmdResult.Ok(
                $"餐桌已扩展为 {size}×{size}，新增 {placementOrigins.Count} 格{suffix}。");
        }

        public override IReadOnlyList<string> GetCompletions(string[] args)
        {
            IReadOnlyList<string> sizes = AvailableSizes();
            return args.Length == 0 ? sizes : Match(sizes, args[args.Length - 1]);
        }

        /// <summary>
        /// 生成逐格扩展顺序。每个新格在加入时都与已有餐桌四向相邻，保证碎片列表从存档重建时不会
        /// 因为“碎片悬空”而被跳过。
        /// </summary>
        internal static bool TryBuildExpansionPlan(
            DiningTable table,
            int size,
            GridPos fragmentLocalCell,
            out List<GridPos> placementOrigins,
            out string error)
        {
            placementOrigins = new List<GridPos>();
            error = string.Empty;
            if (table == null || size <= 0 || !table.TryGetExistingBounds(
                    out int minX,
                    out int minY,
                    out int maxX,
                    out int maxY))
            {
                error = "当前餐桌无有效格子。";
                return false;
            }

            int currentWidth = maxX - minX + 1;
            int currentHeight = maxY - minY + 1;
            if (currentWidth > size || currentHeight > size)
            {
                error = $"当前餐桌包围盒为 {currentWidth}×{currentHeight}，table 命令不会缩小餐桌。";
                return false;
            }

            var existing = new HashSet<GridPos>(table.ExistingCells());
            TableFragmentBuilder.PlacementBounds target =
                TableFragmentBuilder.CenteredBounds(existing, size, size);
            var remaining = new HashSet<GridPos>();
            for (int y = target.MinY; y <= target.MaxY; y++)
            {
                for (int x = target.MinX; x <= target.MaxX; x++)
                {
                    var cell = new GridPos(x, y);
                    if (!existing.Contains(cell))
                    {
                        remaining.Add(cell);
                    }
                }
            }

            while (remaining.Count > 0)
            {
                GridPos next = default;
                bool found = false;
                for (int y = target.MinY; y <= target.MaxY && !found; y++)
                {
                    for (int x = target.MinX; x <= target.MaxX; x++)
                    {
                        var candidate = new GridPos(x, y);
                        if (remaining.Contains(candidate) && Touches(existing, candidate))
                        {
                            next = candidate;
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    placementOrigins.Clear();
                    error = "无法生成连续的餐桌扩展路径。";
                    return false;
                }

                remaining.Remove(next);
                existing.Add(next);
                placementOrigins.Add(new GridPos(
                    next.X - fragmentLocalCell.X,
                    next.Y - fragmentLocalCell.Y));
            }

            return true;
        }

        private static IReadOnlyList<string> AvailableSizes()
        {
            if (!GameRunContext.HasRun)
            {
                return Array.Empty<string>();
            }

            GameRun run = GameRunContext.Current;
            cfg.Character character = run.Tables?.TbCharacter.GetOrDefault(run.CharacterId);
            int maxSize = character != null
                ? Math.Min(character.MaxDiningTableWidth, character.MaxDiningTableHeight)
                : 0;
            return Enumerable.Range(1, maxSize).Select(value => value.ToString()).ToList();
        }

        private static bool Touches(HashSet<GridPos> existing, GridPos cell)
        {
            return existing.Contains(cell.Offset(1, 0))
                || existing.Contains(cell.Offset(-1, 0))
                || existing.Contains(cell.Offset(0, 1))
                || existing.Contains(cell.Offset(0, -1));
        }
    }
}
