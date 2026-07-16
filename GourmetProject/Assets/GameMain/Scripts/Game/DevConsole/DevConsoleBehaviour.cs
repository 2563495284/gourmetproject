#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GourmetProject.Game.DevConsole
{
    /// <summary>
    /// 开发者控制台的 IMGUI 悬浮层宿主：反引号键 ` 切换显隐，顶部输出日志 + 底部输入框，
    /// 支持上下键翻历史、Tab 补全。仅在编辑器 / Development Build 下由
    /// <see cref="Bootstrap"/> 自动创建常驻实例，正式包不含本类型。
    /// 输入切换走新版 Input System（<see cref="Keyboard"/>）；IMGUI 内的键处理走
    /// <see cref="Event.current"/>（与 <c>UnityEngine.Input</c> 无关，符合项目输入规则）。
    /// </summary>
    public sealed class DevConsoleBehaviour : MonoBehaviour
    {
        private const string InputControlName = "DevConsoleInput";
        private const int MaxOutputLines = 200;

        private static DevConsoleBehaviour _instance;

        /// <summary>控制台当前是否展开（供玩法层按需屏蔽世界输入）。</summary>
        public static bool IsOpen => _instance != null && _instance._visible;

        private DevConsole _console;
        private bool _visible;
        private string _input = string.Empty;
        private readonly List<string> _output = new List<string>();
        private Vector2 _scroll;
        private int _historyIndex = -1;
        private bool _focusInput;
        private bool _scrollToBottom;
        private GUIStyle _outputStyle;
        private GUIStyle _inputStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[DevConsole]");
            _instance = go.AddComponent<DevConsoleBehaviour>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _console = new DevConsole();
            Print("GourmetProject 开发者控制台。输入 help 查看命令，按 ` 关闭。");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame)
            {
                _visible = !_visible;
                if (_visible)
                {
                    _focusInput = true;
                }
            }
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            EnsureStyles();
            HandleKeys();
            DrawWindow();

            // 切换用的反引号字符可能漏进输入框，统一剔除。
            if (_input.IndexOf('`') >= 0)
            {
                _input = _input.Replace("`", string.Empty);
            }
        }

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown)
            {
                return;
            }

            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Submit();
                    e.Use();
                    break;
                case KeyCode.Tab:
                    Complete();
                    e.Use();
                    break;
                case KeyCode.UpArrow:
                    NavigateHistory(1);
                    e.Use();
                    break;
                case KeyCode.DownArrow:
                    NavigateHistory(-1);
                    e.Use();
                    break;
                case KeyCode.Escape:
                    _visible = false;
                    e.Use();
                    break;
            }
        }

        private void DrawWindow()
        {
            float height = Mathf.Min(Screen.height * 0.45f, 400f);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(0f, 0f, Screen.width, height), GUI.skin.box);

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _output)
            {
                GUILayout.Label(line, _outputStyle);
            }

            GUILayout.EndScrollView();

            if (_scrollToBottom && Event.current.type == EventType.Repaint)
            {
                _scroll.y = Mathf.Infinity;
                _scrollToBottom = false;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(">", _inputStyle, GUILayout.Width(16f));
            GUI.SetNextControlName(InputControlName);
            _input = GUILayout.TextField(_input, _inputStyle);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            if (_focusInput)
            {
                GUI.FocusControl(InputControlName);
                MoveCursorToEnd();
                _focusInput = false;
            }
        }

        private void Submit()
        {
            string command = _input;
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            Print("> " + command.Trim());
            CmdResult result = _console.ProcessCommand(command);
            if (!string.IsNullOrEmpty(result.Message))
            {
                Print(result.Success ? result.Message : "[错误] " + result.Message);
            }

            _input = string.Empty;
            _historyIndex = -1;
            _focusInput = true;
        }

        private void Complete()
        {
            IReadOnlyList<string> candidates = _console.GetCompletions(_input);
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            if (candidates.Count == 1)
            {
                _input = ReplaceLastToken(_input, candidates[0]) + " ";
            }
            else
            {
                string common = LongestCommonPrefix(candidates);
                if (!string.IsNullOrEmpty(common) && common.Length > LastToken(_input).Length)
                {
                    _input = ReplaceLastToken(_input, common);
                }

                Print("候选：" + string.Join("   ", candidates.Take(40)));
            }

            _focusInput = true;
        }

        private void NavigateHistory(int direction)
        {
            IReadOnlyList<string> history = _console.History;
            if (history.Count == 0)
            {
                return;
            }

            _historyIndex = Mathf.Clamp(_historyIndex + direction, -1, history.Count - 1);
            _input = _historyIndex < 0 ? string.Empty : history[_historyIndex];
            _focusInput = true;
        }

        private void Print(string line)
        {
            _output.Add(line);
            if (_output.Count > MaxOutputLines)
            {
                _output.RemoveRange(0, _output.Count - MaxOutputLines);
            }

            _scrollToBottom = true;
        }

        private void EnsureStyles()
        {
            if (_outputStyle != null)
            {
                return;
            }

            _outputStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                richText = true,
                wordWrap = true,
                normal = { textColor = Color.white },
            };

            _inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 15,
                normal = { textColor = Color.white },
            };
        }

        private static string LastToken(string input)
        {
            if (string.IsNullOrEmpty(input) || input.EndsWith(" "))
            {
                return string.Empty;
            }

            int idx = input.LastIndexOf(' ');
            return idx < 0 ? input : input.Substring(idx + 1);
        }

        private static string ReplaceLastToken(string input, string token)
        {
            if (string.IsNullOrEmpty(input) || input.EndsWith(" "))
            {
                return input + token;
            }

            int idx = input.LastIndexOf(' ');
            return idx < 0 ? token : input.Substring(0, idx + 1) + token;
        }

        private static string LongestCommonPrefix(IReadOnlyList<string> values)
        {
            if (values.Count == 0)
            {
                return string.Empty;
            }

            string prefix = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                string current = values[i];
                int max = Mathf.Min(prefix.Length, current.Length);
                int j = 0;
                while (j < max && char.ToLowerInvariant(prefix[j]) == char.ToLowerInvariant(current[j]))
                {
                    j++;
                }

                prefix = prefix.Substring(0, j);
                if (prefix.Length == 0)
                {
                    break;
                }
            }

            return prefix;
        }

        private static void MoveCursorToEnd()
        {
            if (GUIUtility.keyboardControl == 0)
            {
                return;
            }

            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            editor?.MoveTextEnd();
        }
    }
}
#endif
