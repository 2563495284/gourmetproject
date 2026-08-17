using System;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SemanticDescriptionFormatterTests
    {
        private const string MultiplyMaterialPath =
            "Fonts & Materials/DescriptionMultiplyOutline";
        private const string FoodSkillDescriptionPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Tooltips/FoodSkillDescriptionView.prefab";
        private const string FoodFlavorDetailPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Tooltips/FoodFlavorDetailView.prefab";

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("普通描述", "普通描述")]
        [TestCase(
            "[context]首个结算[/context]时",
            "<b><color=#137A4A>首个结算</color></b>时")]
        [TestCase(
            "[strong]分数[/strong] [score]+3×层数[/score]",
            "<b>分数</b> <b><color=#28669C>+3×层数</color></b>")]
        [TestCase(
            "[strong]倍率[/strong] [multadd]+0.5[/multadd]",
            "<b>倍率</b> <b><color=#B23A48>+0.5</color></b>")]
        [TestCase(
            "[strong]倍率[/strong] [multmul]×(0.02×层数)[/multmul]",
            "<b>倍率</b> <material=\"DescriptionMultiplyOutline\"><b><color=#FFFFFF>×(0.02×层数)</color></b></material>")]
        [TestCase(
            "额外获得[gold]金币+150[/gold]",
            "额外获得<b><color=#9A6500>金币+150</color></b>")]
        [TestCase(
            "[term]丢弃[/term]次数+1",
            "<b><color=#7656A8>丢弃</color></b>次数+1")]
        [TestCase(
            "[benefit]额外视为[/benefit]3份食物",
            "<b><color=#7656A8>额外视为</color></b>3份食物")]
        [TestCase("[strong]份数[/strong]", "<b>份数</b>")]
        public void Format_ConvertsSupportedSemanticTags(string source, string expected)
        {
            Assert.That(SemanticDescriptionFormatter.Format(source), Is.EqualTo(expected));
        }

        [Test]
        public void Format_AdjacentAndMultilineTags_KeepIndependentStyles()
        {
            const string source =
                "[context]所有[/context][term]装饰品[/term]\n" +
                "[strong]分数[/strong] [score]+20[/score]";
            const string expected =
                "<b><color=#137A4A>所有</color></b><b><color=#7656A8>装饰品</color></b>\n" +
                "<b>分数</b> <b><color=#28669C>+20</color></b>";

            Assert.That(SemanticDescriptionFormatter.Format(source), Is.EqualTo(expected));
        }

        [TestCase("保留[unknown]文本[/unknown]")]
        [TestCase("保留[score文本")]
        [TestCase("保留[/score文本")]
        public void Format_UnknownOrIncompleteSquareTags_RemainLiteral(string source)
        {
            Assert.That(SemanticDescriptionFormatter.Format(source), Is.EqualTo(source));
        }

        [Test]
        public void Format_UnknownTagsRemainLiteralWhileKnownTagsAreConverted()
        {
            const string source =
                "[unknown]说明[/unknown] [strong]分数[/strong] [score]+20[/score]";
            const string expected =
                "[unknown]说明[/unknown] <b>分数</b> <b><color=#28669C>+20</color></b>";

            Assert.That(SemanticDescriptionFormatter.Format(source), Is.EqualTo(expected));
        }

        [TestCase("[score]+20")]
        [TestCase("+20[/score]")]
        [TestCase("[score]+20[/gold]")]
        [TestCase("[context]所有[term]装饰品[/term][/context]")]
        [TestCase("[strong]分数[score]+20[/score][/strong]")]
        public void Format_InvalidKnownTagStructure_LeavesWholeSourceUnchanged(string source)
        {
            Assert.That(SemanticDescriptionFormatter.Format(source), Is.EqualTo(source));
        }

        [Test]
        public void Format_PreservesExistingTmpAndAngleBracketText()
        {
            const string source =
                "<i>既有样式</i> 巧克力棒<甜蜜传递> [term]甜蜜传递[/term]";
            const string expected =
                "<i>既有样式</i> 巧克力棒<甜蜜传递> <b><color=#7656A8>甜蜜传递</color></b>";

            Assert.That(SemanticDescriptionFormatter.Format(source), Is.EqualTo(expected));
        }

        [Test]
        public void Format_AlreadyFormattedText_IsIdempotent()
        {
            const string source =
                "[strong]分数[/strong] [score]+20[/score]，" +
                "[strong]倍率[/strong] [multmul]×1.8[/multmul]";
            string once = SemanticDescriptionFormatter.Format(source);

            Assert.That(SemanticDescriptionFormatter.Format(once), Is.EqualTo(once));
        }

        [Test]
        public void Format_AfterPlaceholderComposition_StylesResolvedValue()
        {
            string composed = EffectDescFormatter.Format(
                "[strong]分数[/strong] [score]{0}[/score]",
                new[] { 20f },
                signed: true);

            Assert.That(
                SemanticDescriptionFormatter.Format(composed),
                Is.EqualTo("<b>分数</b> <b><color=#28669C>+20</color></b>"));
        }

        [Test]
        public void SkillDescriptionComposition_StylesOnlyScopeCoreAndResolvedScore()
        {
            var rule = new SkillRuleDef(
                "rule",
                "skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Adjacent,
                2,
                new[] { 20f },
                Array.Empty<string>());

            string composed = SkillDescComposer.ComposeComponent(
                "{ascope}食物[strong]分数[/strong] [score]{0}[/score]",
                rule,
                signed: true);

            Assert.That(
                composed,
                Is.EqualTo(
                    "[context]相邻[/context] 2 个食物" +
                    "[strong]分数[/strong] [score]+20[/score]"));
            Assert.That(
                SemanticDescriptionFormatter.Format(composed),
                Is.EqualTo(
                    "<b><color=#137A4A>相邻</color></b> 2 个食物<b>分数</b> " +
                    "<b><color=#28669C>+20</color></b>"));
        }

        [Test]
        public void SkillDescriptionComposition_AllWithRandomCount_KeepsCountWithoutInventingScope()
        {
            var rule = new SkillRuleDef(
                "rule",
                "skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.TransferSkills,
                SkillScope.All,
                2,
                new[] { 0f },
                Array.Empty<string>());

            Assert.That(
                SkillDescComposer.ComposeComponent("{ascope}食物", rule, signed: false),
                Is.EqualTo("2 个食物"));
        }

        [Test]
        public void SkillDescriptionComposition_StylesDynamicCakeDefinition()
        {
            var rule = new SkillRuleDef(
                "rule",
                "skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddMult,
                SkillScope.Category,
                0,
                new[] { 1.5f },
                new[] { "cat:cake" });

            Assert.That(
                SkillDescComposer.ComposeComponent(
                    "{cat}类食物[strong]倍率[/strong] [multmul]×{0}[/multmul]",
                    rule,
                    signed: false),
                Is.EqualTo(
                    "[term]蛋糕[/term]类食物" +
                    "[strong]倍率[/strong] [multmul]×1.5[/multmul]"));
        }

        [Test]
        public void SkillDescriptionComposition_KeepsUnknownDynamicCategoryPlain()
        {
            var rule = new SkillRuleDef(
                "rule",
                "skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddMult,
                SkillScope.Category,
                0,
                new[] { 1.5f },
                new[] { "cat:风味" });

            Assert.That(
                SkillDescComposer.ComposeComponent(
                    "{cat}类食物[strong]倍率[/strong] [multmul]×{0}[/multmul]",
                    rule,
                    signed: false),
                Is.EqualTo(
                    "风味类食物[strong]倍率[/strong] " +
                    "[multmul]×1.5[/multmul]"));
        }

        [Test]
        public void SkillDescriptionComposition_StylesDynamicServingCountWithoutColor()
        {
            var rule = new SkillRuleDef(
                "rule",
                "skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.None,
                SkillScope.Self,
                0,
                Array.Empty<float>(),
                new[] { "cat:份数" });

            string composed = SkillDescComposer.ComposeComponent("{cat}", rule, signed: false);

            Assert.That(composed, Is.EqualTo("[strong]份数[/strong]"));
            Assert.That(
                SemanticDescriptionFormatter.Format(composed),
                Is.EqualTo("<b>份数</b>"));
        }

        [Test]
        public void Set_EnablesRichTextAndAssignsFormattedContent()
        {
            var gameObject = new GameObject(
                "SemanticDescriptionFormatterTest",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));

            try
            {
                var target = gameObject.GetComponent<TextMeshProUGUI>();
                target.richText = false;

                SemanticDescriptionFormatter.Set(target, "[gold]15金币[/gold]");

                Assert.That(target.richText, Is.True);
                Assert.That(target.text, Is.EqualTo("<b><color=#9A6500>15金币</color></b>"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Set_NullTarget_ThrowsClearly()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SemanticDescriptionFormatter.Set(null, "描述"));
        }

        [Test]
        public void FoodSkillDescriptionBind_FormatsSemanticMarkup()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                FoodSkillDescriptionPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                FoodSkillDescriptionView view = instance.GetComponent<FoodSkillDescriptionView>();
                TMP_Text target = instance.GetComponentInChildren<TMP_Text>(true);
                Assert.That(view, Is.Not.Null);
                Assert.That(target, Is.Not.Null);
                target.richText = false;

                view.Bind(
                    "[context]上侧及自身[/context]\n" +
                    "[strong]倍率[/strong] [multadd]+0.5[/multadd]");

                Assert.That(target.richText, Is.True);
                Assert.That(
                    target.text,
                    Is.EqualTo(
                        "<b><color=#137A4A>上侧及自身</color></b>\n" +
                        "<b>倍率</b> <b><color=#B23A48>+0.5</color></b>"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [TestCase(
            "[benefit]额外结算[/benefit]",
            "<b><color=#7656A8>额外结算</color></b>")]
        [TestCase(
            "[context]结算开始[/context]\n[strong]倍率[/strong] [multadd]+0.5[/multadd]",
            "<b><color=#137A4A>结算开始</color></b>\n<b>倍率</b> <b><color=#B23A48>+0.5</color></b>")]
        public void FoodFlavorDetailBind_FormatsSemanticMarkup(string source, string expected)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                FoodFlavorDetailPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                FoodFlavorDetailView view = instance.GetComponent<FoodFlavorDetailView>();
                Assert.That(view, Is.Not.Null);

                var serializedView = new SerializedObject(view);
                TMP_Text target = serializedView.FindProperty("_descriptionText")
                    .objectReferenceValue as TMP_Text;
                Assert.That(target, Is.Not.Null);
                target.richText = false;

                view.Bind("咸", source);

                Assert.That(target.richText, Is.True);
                Assert.That(target.text, Is.EqualTo(expected));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void MultiplyMaterial_UsesDefaultFontAtlasAndApprovedOutline()
        {
            Material material = Resources.Load<Material>(MultiplyMaterialPath);
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;

            Assert.That(material, Is.Not.Null);
            Assert.That(font, Is.Not.Null);
            Assert.That(material.shader, Is.EqualTo(font.material.shader));
            Assert.That(material.mainTexture, Is.EqualTo(font.material.mainTexture));
            Assert.That(material.IsKeywordEnabled(ShaderUtilities.Keyword_Outline), Is.True);
            Assert.That(material.GetFloat(ShaderUtilities.ID_OutlineWidth), Is.EqualTo(0.2f).Within(0.0001f));
            AssertColor(
                material.GetColor(ShaderUtilities.ID_OutlineColor),
                new Color32(0xB2, 0x3A, 0x48, 0xFF));
            AssertColor(material.GetColor(ShaderUtilities.ID_FaceColor), Color.white);
        }

        [Test]
        public void MultiplyTag_UsesOnlyTheOutlinedSubMaterialAndKeepsSurroundingTextDefault()
        {
            var gameObject = new GameObject(
                "SemanticDescriptionMultiplyMaterialTest",
                typeof(TextMeshPro));

            try
            {
                var target = gameObject.GetComponent<TextMeshPro>();
                target.font = TMP_Settings.defaultFontAsset;
                SemanticDescriptionFormatter.Set(
                    target,
                    "前[multmul]×1.8[/multmul]后");

                target.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);

                Assert.That(target.textInfo.characterCount, Is.EqualTo(6));
                // “×”由 TMP 后备字体提供时会多生成一个同样带描边的材质引用。
                Assert.That(target.textInfo.materialCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(target.textInfo.characterInfo[0].materialReferenceIndex, Is.Zero);
                Assert.That(target.textInfo.characterInfo[5].materialReferenceIndex, Is.Zero);

                for (int i = 1; i <= 4; i++)
                {
                    TMP_CharacterInfo character = target.textInfo.characterInfo[i];
                    Assert.That(character.materialReferenceIndex, Is.GreaterThan(0));
                    Assert.That(
                        character.style & FontStyles.Bold,
                        Is.EqualTo(FontStyles.Bold));
                    Assert.That(character.color, Is.EqualTo(new Color32(255, 255, 255, 255)));
                    Assert.That(
                        character.material.IsKeywordEnabled(ShaderUtilities.Keyword_Outline),
                        Is.True);
                    Assert.That(
                        character.material.GetFloat(ShaderUtilities.ID_OutlineWidth),
                        Is.EqualTo(0.2f).Within(0.0001f));
                    AssertColor(
                        character.material.GetColor(ShaderUtilities.ID_OutlineColor),
                        new Color32(0xB2, 0x3A, 0x48, 0xFF));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }
    }
}
