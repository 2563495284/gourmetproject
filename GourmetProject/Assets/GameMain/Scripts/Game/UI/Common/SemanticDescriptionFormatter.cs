using System;
using System.Text;
using TMPro;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>
    /// 将配置中的语义描述标签转换为 TextMeshPro 富文本。
    /// 标签必须成对且不能嵌套；无效标记会保留原文，避免产生局部样式泄漏。
    /// </summary>
    public static class SemanticDescriptionFormatter
    {
        public const string ContextColor = "#137A4A";
        public const string ScoreColor = "#28669C";
        public const string PermanentScoreColor = "#337DB5";
        public const string MultiplierAddColor = "#B23A48";
        public const string GoldColor = "#9A6500";
        public const string TermColor = "#7656A8";
        public const string MultiplyFaceColor = "#E15A64";
        public const string MultiplyMaterialName = "DescriptionMultiplyOutline";

        private static readonly TagDefinition[] Definitions =
        {
            new TagDefinition("strong", "<b>", "</b>"),
            Colored("context", ContextColor),
            Colored("score", ScoreColor),
            Colored("scoreperm", PermanentScoreColor),
            Colored("multadd", MultiplierAddColor),
            new TagDefinition(
                "multmul",
                $"<material=\"{MultiplyMaterialName}\"><b><color={MultiplyFaceColor}>",
                "</color></b></material>"),
            Colored("gold", GoldColor),
            Colored("term", TermColor),
            Colored("benefit", TermColor),
        };

        /// <summary>将语义标签转换为 TMP 富文本；null 会转换为空字符串。</summary>
        public static string Format(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source ?? string.Empty;
            }

            // 已格式化的结果不含语义方括号，直接返回也保证重复调用不改变结果。
            if (source.IndexOf('[') < 0)
            {
                return source;
            }

            if (!HasValidSemanticStructure(source))
            {
                return source;
            }

            var result = new StringBuilder(source.Length + 32);
            int index = 0;
            while (index < source.Length)
            {
                if (TryCopyAngleBracketSpan(source, index, result, out int nextIndex))
                {
                    index = nextIndex;
                    continue;
                }

                if (TryReadTag(source, index, out TagDefinition definition, out bool isClosing, out int tagLength))
                {
                    result.Append(isClosing ? definition.Suffix : definition.Prefix);
                    index += tagLength;
                    continue;
                }

                result.Append(source[index]);
                index++;
            }

            return result.ToString();
        }

        /// <summary>开启富文本并将格式化后的描述写入目标文本组件。</summary>
        public static void Set(TMP_Text target, string source)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.richText = true;
            target.text = Format(source);
        }

        private static TagDefinition Colored(string name, string color)
        {
            return new TagDefinition(name, $"<b><color={color}>", "</color></b>");
        }

        private static bool HasValidSemanticStructure(string source)
        {
            TagDefinition openDefinition = null;
            int index = 0;

            while (index < source.Length)
            {
                if (TrySkipAngleBracketSpan(source, index, out int nextIndex))
                {
                    index = nextIndex;
                    continue;
                }

                if (!TryReadTag(source, index, out TagDefinition definition, out bool isClosing, out int tagLength))
                {
                    index++;
                    continue;
                }

                if (isClosing)
                {
                    if (openDefinition != definition)
                    {
                        return false;
                    }

                    openDefinition = null;
                }
                else
                {
                    if (openDefinition != null)
                    {
                        return false;
                    }

                    openDefinition = definition;
                }

                index += tagLength;
            }

            return openDefinition == null;
        }

        private static bool TryReadTag(
            string source,
            int index,
            out TagDefinition definition,
            out bool isClosing,
            out int tagLength)
        {
            definition = null;
            isClosing = false;
            tagLength = 0;

            if (index < 0 || index >= source.Length || source[index] != '[')
            {
                return false;
            }

            int nameStart = index + 1;
            if (nameStart < source.Length && source[nameStart] == '/')
            {
                isClosing = true;
                nameStart++;
            }

            int closingBracket = source.IndexOf(']', nameStart);
            if (closingBracket < 0)
            {
                return false;
            }

            int nameLength = closingBracket - nameStart;
            for (int i = 0; i < Definitions.Length; i++)
            {
                TagDefinition candidate = Definitions[i];
                if (candidate.Name.Length == nameLength &&
                    string.CompareOrdinal(source, nameStart, candidate.Name, 0, nameLength) == 0)
                {
                    definition = candidate;
                    tagLength = closingBracket - index + 1;
                    return true;
                }
            }

            return false;
        }

        private static bool TryCopyAngleBracketSpan(
            string source,
            int index,
            StringBuilder destination,
            out int nextIndex)
        {
            if (!TrySkipAngleBracketSpan(source, index, out nextIndex))
            {
                return false;
            }

            destination.Append(source, index, nextIndex - index);
            return true;
        }

        private static bool TrySkipAngleBracketSpan(string source, int index, out int nextIndex)
        {
            nextIndex = index;
            if (source[index] != '<')
            {
                return false;
            }

            int closingBracket = source.IndexOf('>', index + 1);
            if (closingBracket < 0)
            {
                return false;
            }

            nextIndex = closingBracket + 1;
            return true;
        }

        private sealed class TagDefinition
        {
            public TagDefinition(string name, string prefix, string suffix)
            {
                Name = name;
                Prefix = prefix;
                Suffix = suffix;
            }

            public string Name { get; }
            public string Prefix { get; }
            public string Suffix { get; }
        }
    }
}
