using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GpBoard = GourmetProject.Gameplay.Board.Board;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 根据局外 Run 状态构建一场局内战斗，避免 GameRun 同时承担战斗装配细节。
    /// </summary>
    public static class BattleSessionFactory
    {
        public static BattleSession Build(GameRun run, int requiredScore, string modifier, string key)
        {
            modifier ??= string.Empty;

            cfg.Character character = run.Tables.TbCharacter.GetOrDefault(run.CharacterId);
            string recipeId = character?.InitialRecipeId;
            RecipeDef recipe = run.Database.GetRecipe(recipeId);

            var slots = new List<RecipeSlot>(GameRun.RecipeSlotCount);
            if (recipe != null)
            {
                var recipeStream = GameApp.Random.Stream($"recipe_{key}");
                for (int i = 0; i < GameRun.RecipeSlotCount; i++)
                {
                    List<string> deck = RecipeRoller.Roll(recipe, run.Database, recipeStream);
                    foreach (string dishId in run.BonusDishIds)
                    {
                        if (run.Database.GetDish(dishId) != null)
                        {
                            deck.Add(dishId);
                        }
                    }

                    slots.Add(new RecipeSlot($"菜谱{i + 1}", deck));
                }
            }
            else
            {
                Log.Warning($"Character '{run.CharacterId}' has no valid recipe '{recipeId}'.", "GameRun");
            }

            GpBoard board = BuildBoard(run, character, modifier);

            var battleStream = GameApp.Random.Stream($"battle_{key}");
            var session = new BattleSession(board, run.Database, battleStream, slots, requiredScore);

            if (modifier == "limit_serve")
            {
                session.MaxServes = 5;
            }

            ApplyPassiveItems(run, session);
            return session;
        }

        /// <summary>
        /// 由角色配置构建本局胃部棋盘：初始胃形状取自碎片库，最大包围盒取角色 max 尺寸。
        /// Boss「small_board」修正收缩最大包围盒（初始碎片超出部分自动裁掉）。
        /// </summary>
        private static GpBoard BuildBoard(GameRun run, cfg.Character character, string modifier)
        {
            int maxW = character != null && character.MaxStomachWidth > 0 ? character.MaxStomachWidth : GameRun.BoardWidth;
            int maxH = character != null && character.MaxStomachHeight > 0 ? character.MaxStomachHeight : GameRun.BoardHeight;

            if (modifier == "small_board")
            {
                maxW = System.Math.Min(maxW, 3);
                maxH = System.Math.Min(maxH, 3);
            }

            StomachFragmentDef fragment = run.Database.GetFragment(character?.InitialFragmentId);
            if (fragment == null)
            {
                Log.Warning($"Character '{run.CharacterId}' 无有效初始胃碎片 '{character?.InitialFragmentId}'，回退为满 {maxW}x{maxH} 棋盘。", "GameRun");
                return new GpBoard(maxW, maxH);
            }

            return StomachBuilder.BuildExpanded(fragment, GetAcquiredFragments(run), maxW, maxH);
        }

        private static List<StomachFragmentDef> GetAcquiredFragments(GameRun run)
        {
            var fragments = new List<StomachFragmentDef>(run.StomachFragmentIds.Count);
            foreach (string fragmentId in run.StomachFragmentIds)
            {
                StomachFragmentDef fragment = run.Database.GetFragment(fragmentId);
                if (fragment != null)
                {
                    fragments.Add(fragment);
                }
            }

            return fragments;
        }

        /// <summary>把被动道具效果汇总成局级修正注入战斗会话。</summary>
        private static void ApplyPassiveItems(GameRun run, BattleSession session)
        {
            foreach (RunItemState state in run.Items)
            {
                cfg.Item item = run.Tables.TbItem.GetOrDefault(state.ItemId);
                if (item == null || item.Kind != cfg.ItemKind.Passive)
                {
                    continue;
                }

                PassiveItemEffectRegistry.ApplyToBattle(session, item, state);
            }
        }
    }
}
