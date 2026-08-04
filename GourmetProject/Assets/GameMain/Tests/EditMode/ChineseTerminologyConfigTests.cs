using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using GourmetProject.Config;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ChineseTerminologyConfigTests
    {
        private static readonly Regex Forbidden = new(
            "美食(?!街)|菜品|菜谱|出餐|战斗|品鉴|美味度|最终分数|目标分|" +
            "餐桌碎片|胃部碎片|餐桌格子|行动轴|日程轴|日常行动|随机行动|节点事件|" +
            "被动道具|主动道具|负面道具|道具|角色|拾取|乘区|倍率倍率|" +
            "经营挑战挑战|食物挑战|扣心|生命耗尽|恢复血量|一道菜|\\d+\\s*(?:个|道)菜|" +
            "本局胜利|单局胜利|本局失败|单局失败|完整\\s*Run|普通/超级|普通、超级|\\bBoss\\b",
            RegexOptions.Compiled);

        private cfg.Tables _tables;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
        }

        [Test]
        public void RuntimeConfig_PlayerFacingChineseUsesCanonicalTerms()
        {
            var violations = new List<string>();
            foreach ((string path, string value) in EnumerateConfigStrings(_tables))
            {
                Match match = Forbidden.Match(value ?? string.Empty);
                if (match.Success)
                {
                    violations.Add($"{path}: '{match.Value}' in '{value}'");
                }
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void ActionNames_UseFormalPlayerFacingNames()
        {
            Assert.That(_tables.TbAction.Get("act_food_gold").Name, Is.EqualTo("日常营业"));
            Assert.That(_tables.TbAction.Get("act_food_hard_gold").Name, Is.EqualTo("火热营业"));
            Assert.That(_tables.TbAction.Get("act_boss").Name, Is.EqualTo("星级评鉴"));
            Assert.That(_tables.TbAction.Get("act_event").Name, Is.EqualTo("突发事件"));
            Assert.That(_tables.TbAction.Get("act_reward").Name, Is.EqualTo("幸运事件"));
            Assert.That(_tables.TbAction.Get("act_shop").Name, Is.EqualTo("商店采购"));
            Assert.That(_tables.TbAction.Get("act_interest").Name, Is.EqualTo("收取利息"));
        }

        private static IEnumerable<(string Path, string Value)> EnumerateConfigStrings(cfg.Tables tables)
        {
            foreach (PropertyInfo tableProperty in typeof(cfg.Tables).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object table = tableProperty.GetValue(tables);
                PropertyInfo dataListProperty = table?.GetType().GetProperty("DataList", BindingFlags.Public | BindingFlags.Instance);
                if (dataListProperty?.GetValue(table) is not IEnumerable rows)
                {
                    continue;
                }

                int rowIndex = 0;
                foreach (object row in rows)
                {
                    if (row == null)
                    {
                        continue;
                    }

                    foreach (PropertyInfo property in row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        object value = property.GetValue(row);
                        if (value is string text)
                        {
                            yield return ($"{tableProperty.Name}[{rowIndex}].{property.Name}", text);
                        }
                        else if (value is IEnumerable<string> strings)
                        {
                            int valueIndex = 0;
                            foreach (string entry in strings)
                            {
                                yield return ($"{tableProperty.Name}[{rowIndex}].{property.Name}[{valueIndex}]", entry);
                                valueIndex++;
                            }
                        }
                    }

                    rowIndex++;
                }
            }
        }
    }
}
