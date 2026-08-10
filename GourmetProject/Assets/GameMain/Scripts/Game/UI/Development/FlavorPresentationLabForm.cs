using System;
using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Development
{
    /// <summary>独立的多风味 Sprite 表现实验室，不读取或修改当前 GameRun。</summary>
    public sealed class FlavorPresentationLabForm : UGuiForm
    {
        private static readonly Color Paper = new(0.96f, 0.88f, 0.69f, 1f);
        private static readonly Color Ink = new(0.25f, 0.12f, 0.045f, 1f);
        private static readonly Color ButtonFill = new(0.86f, 0.64f, 0.27f, 1f);
        private static readonly Color InactiveFlavor = new(0.46f, 0.39f, 0.29f, 0.82f);
        private static readonly string[] PreviewTitles = { "旧脏印", "有机味区", "分段味环", "味觉气场" };
        private static readonly string[] DishPaths =
        {
            "Sprites/Dishes/jelly",
            "Sprites/Dishes/cupcake",
            "Sprites/Dishes/chocolate_strip",
        };
        private static readonly string[] DishNames = { "果冻", "糖霜蛋糕", "巧克力条" };

        private readonly RawImage[] _previews = new RawImage[4];
        private readonly Button[] _flavorButtons = new Button[FlavorVisualCatalog.Capacity];
        private readonly List<string> _flavorIds = new(FlavorVisualCatalog.Capacity);
        private FlavorPrototypePreviewRig _rig;
        private TMP_Text _dishLabel;
        private TMP_Text _animationLabel;
        private TMP_Text _intensityLabel;
        private TMP_Text _status;
        private bool _built;
        private bool _paused;
        private int _dishIndex;
        private int _flavorMask;
        private float _intensity = 0.72f;

        internal bool PreviewRigActive => _rig != null;

        internal int CurrentFlavorMask => _flavorMask;

        internal int CurrentDishIndex => _dishIndex;

        internal int ActiveParticleSystemCount => _rig?.ActiveParticleSystemCount ?? 0;

        internal int PlayingParticleSystemCount => _rig?.PlayingParticleSystemCount ?? 0;

        internal float MotionTimeForTests => _rig?.MotionTime ?? 0f;

        internal IReadOnlyList<RenderTexture> PreviewTexturesForTests => _rig?.Textures;

        internal bool PreviewTexturesCreated
        {
            get
            {
                if (_rig == null)
                {
                    return false;
                }

                IReadOnlyList<RenderTexture> textures = _rig.Textures;
                if (textures.Count != _previews.Length)
                {
                    return false;
                }

                for (int i = 0; i < textures.Count; i++)
                {
                    if (textures[i] == null || !textures[i].IsCreated())
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            BuildVisualTree();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            ResetLab();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            ReleaseRig();
            base.OnClose(isShutdown, userData);
        }

        private void OnDestroy()
        {
            ReleaseRig();
        }

        internal void OpenPreviewForTests()
        {
            BuildVisualTree();
            ResetLab();
        }

        internal void ClosePreviewForTests()
        {
            ReleaseRig();
        }

        internal void SetFlavorMaskForTests(int mask)
        {
            SetFlavorMask(mask);
        }

        internal void CycleDishForTests()
        {
            CycleDish();
        }

        internal void ToggleAnimationForTests()
        {
            ToggleAnimation();
        }

        private void BuildVisualTree()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            Image background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
            }

            background.color = new Color(0.12f, 0.07f, 0.035f, 0.98f);
            background.raycastTarget = true;

            RectTransform paper = CreateRect("Paper", transform, new Vector2(0.025f, 0.025f), new Vector2(0.975f, 0.975f));
            Image paperImage = paper.gameObject.AddComponent<Image>();
            paperImage.color = Paper;
            paperImage.raycastTarget = true;
            Outline outline = paper.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(5f, -5f);

            TMP_Text title = CreateText("Title", paper, "多风味 Sprite 表现实验室", 38f, TextAlignmentOptions.Center);
            SetRect(title.rectTransform, new Vector2(0.19f, 0.91f), new Vector2(0.81f, 0.98f));
            title.fontStyle = FontStyles.Bold;
            TMP_Text subtitle = CreateText(
                "Subtitle",
                paper,
                "同一真实 Sprite · 固定种子 · 不修改对局或正式 FlavorStain",
                19f,
                TextAlignmentOptions.Center);
            SetRect(subtitle.rectTransform, new Vector2(0.2f, 0.875f), new Vector2(0.8f, 0.915f));

            Button close = CreateButton("Close", paper, "关闭", Close);
            SetRect((RectTransform)close.transform, new Vector2(0.865f, 0.915f), new Vector2(0.955f, 0.968f));

            RectTransform previewHost = CreateRect("Previews", paper, new Vector2(0.035f, 0.445f), new Vector2(0.965f, 0.865f));
            var previewGrid = previewHost.gameObject.AddComponent<GridLayoutGroup>();
            previewGrid.padding = new RectOffset(10, 10, 8, 8);
            previewGrid.spacing = new Vector2(14f, 0f);
            previewGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            previewGrid.constraintCount = 4;
            previewGrid.cellSize = new Vector2(420f, 420f);
            previewGrid.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < _previews.Length; i++)
            {
                _previews[i] = CreatePreviewCard(previewHost, PreviewTitles[i], i);
            }

            RectTransform flavors = CreateRect("FlavorControls", paper, new Vector2(0.05f, 0.345f), new Vector2(0.95f, 0.43f));
            var flavorGrid = flavors.gameObject.AddComponent<GridLayoutGroup>();
            flavorGrid.padding = new RectOffset(4, 4, 4, 4);
            flavorGrid.spacing = new Vector2(14f, 6f);
            flavorGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            flavorGrid.constraintCount = 7;
            flavorGrid.cellSize = new Vector2(212f, 70f);
            flavorGrid.childAlignment = TextAnchor.MiddleCenter;
            IReadOnlyList<FlavorVisualDescriptor> descriptors = FlavorVisualCatalog.Descriptors;
            for (int i = 0; i < descriptors.Count; i++)
            {
                int flavorIndex = i;
                _flavorButtons[i] = CreateButton(
                    $"Flavor_{descriptors[i].Kind}",
                    flavors,
                    descriptors[i].DisplayName,
                    () => ToggleFlavor(flavorIndex));
            }

            RectTransform presets = CreateRect("Presets", paper, new Vector2(0.05f, 0.255f), new Vector2(0.95f, 0.335f));
            var presetGrid = presets.gameObject.AddComponent<GridLayoutGroup>();
            presetGrid.padding = new RectOffset(4, 4, 4, 4);
            presetGrid.spacing = new Vector2(14f, 6f);
            presetGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            presetGrid.constraintCount = 6;
            presetGrid.cellSize = new Vector2(252f, 64f);
            presetGrid.childAlignment = TextAnchor.MiddleCenter;
            CreateButton("PresetNone", presets, "无风味", () => SetFlavorMask(0));
            CreateButton("PresetSweet", presets, "单味：甜", () => SetFlavorMask(1 << 0));
            CreateButton("PresetPair", presets, "双味：酸甜", () => SetFlavorMask((1 << 0) | (1 << 1)));
            CreateButton("PresetFour", presets, "四味混合", () => SetFlavorMask((1 << 0) | (1 << 2) | (1 << 3) | (1 << 4)));
            CreateButton("PresetSix", presets, "现有六味", () => SetFlavorMask((1 << 6) - 1));
            CreateButton("PresetSeven", presets, "全部七味", () => SetFlavorMask(FlavorVisualCatalog.AllMask));

            RectTransform actions = CreateRect("Actions", paper, new Vector2(0.05f, 0.13f), new Vector2(0.95f, 0.24f));
            var actionGrid = actions.gameObject.AddComponent<GridLayoutGroup>();
            actionGrid.padding = new RectOffset(4, 4, 4, 4);
            actionGrid.spacing = new Vector2(14f, 6f);
            actionGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            actionGrid.constraintCount = 6;
            actionGrid.cellSize = new Vector2(252f, 78f);
            actionGrid.childAlignment = TextAnchor.MiddleCenter;

            Button dish = CreateButton("CycleDish", actions, string.Empty, CycleDish);
            _dishLabel = dish.GetComponentInChildren<TMP_Text>();
            Button animation = CreateButton("Animation", actions, string.Empty, ToggleAnimation);
            _animationLabel = animation.GetComponentInChildren<TMP_Text>();
            CreateButton("Burst", actions, "粒子爆发", () => _rig?.Burst());
            CreateButton("IntensityDown", actions, "强度 −", () => AdjustIntensity(-0.1f));
            CreateButton("IntensityUp", actions, "强度 +", () => AdjustIntensity(0.1f));
            Button intensity = CreateButton("IntensityLabel", actions, string.Empty, () => { });
            intensity.interactable = false;
            _intensityLabel = intensity.GetComponentInChildren<TMP_Text>();

            _status = CreateText("Status", paper, string.Empty, 21f, TextAlignmentOptions.Center);
            SetRect(_status.rectTransform, new Vector2(0.055f, 0.045f), new Vector2(0.945f, 0.12f));
        }

        private RawImage CreatePreviewCard(Transform parent, string title, int index)
        {
            RectTransform card = CreateRect($"Preview_{index}", parent, Vector2.zero, Vector2.one);
            Image background = card.gameObject.AddComponent<Image>();
            background.color = new Color(1f, 0.97f, 0.84f, 0.94f);
            Outline outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(3f, -3f);

            TMP_Text label = CreateText("Label", card, title, 26f, TextAlignmentOptions.Center);
            SetRect(label.rectTransform, new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.98f));
            label.fontStyle = FontStyles.Bold;

            RectTransform imageRect = CreateRect("Output", card, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.84f));
            var rawImage = imageRect.gameObject.AddComponent<RawImage>();
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;
            rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            return rawImage;
        }

        private void ResetLab()
        {
            _paused = false;
            _dishIndex = 0;
            _intensity = 0.72f;
            ReleaseRig();
            _rig = FlavorPrototypePreviewRig.Create();
            IReadOnlyList<RenderTexture> textures = _rig.Textures;
            for (int i = 0; i < _previews.Length; i++)
            {
                _previews[i].texture = textures[i];
            }

            _rig.SetIntensity(_intensity);
            SetFlavorMask((1 << 0) | (1 << 1));
            UpdateLabels();
        }

        private void ToggleFlavor(int flavorIndex)
        {
            SetFlavorMask(_flavorMask ^ (1 << flavorIndex));
        }

        private void SetFlavorMask(int mask)
        {
            _flavorMask = mask & FlavorVisualCatalog.AllMask;
            FlavorVisualCatalog.FlavorIdsForMask(_flavorMask, _flavorIds);
            _rig?.Bind(LoadCurrentDish(), _flavorIds);
            UpdateFlavorButtons();
            UpdateStatus();
        }

        private void CycleDish()
        {
            _dishIndex = (_dishIndex + 1) % DishPaths.Length;
            _rig?.Bind(LoadCurrentDish(), _flavorIds);
            UpdateLabels();
        }

        private void ToggleAnimation()
        {
            _paused = !_paused;
            _rig?.SetPaused(_paused);
            UpdateLabels();
        }

        private void AdjustIntensity(float delta)
        {
            _intensity = Mathf.Clamp(_intensity + delta, 0.2f, 1f);
            _rig?.SetIntensity(_intensity);
            UpdateLabels();
        }

        private void UpdateFlavorButtons()
        {
            IReadOnlyList<FlavorVisualDescriptor> descriptors = FlavorVisualCatalog.Descriptors;
            for (int i = 0; i < _flavorButtons.Length; i++)
            {
                Button button = _flavorButtons[i];
                if (button == null)
                {
                    continue;
                }

                bool active = (_flavorMask & (1 << i)) != 0;
                Image image = button.GetComponent<Image>();
                image.color = active ? descriptors[i].Color : InactiveFlavor;
                TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                label.text = active ? $"开 · {descriptors[i].DisplayName}" : descriptors[i].DisplayName;
                label.color = active && i == (int)FlavorVisualKind.Salty ? Ink : Color.white;
            }
        }

        private void UpdateLabels()
        {
            if (_dishLabel != null)
            {
                _dishLabel.text = $"食物：{DishNames[_dishIndex]}";
            }

            if (_animationLabel != null)
            {
                _animationLabel.text = _paused ? "动画：暂停" : "动画：播放";
            }

            if (_intensityLabel != null)
            {
                _intensityLabel.text = $"强度 {_intensity:0.0}";
            }

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_status == null)
            {
                return;
            }

            if (_flavorMask == 0)
            {
                _status.text = $"{DishNames[_dishIndex]} · 无风味基线";
                return;
            }

            var names = new List<string>(_flavorIds.Count);
            for (int i = 0; i < _flavorIds.Count; i++)
            {
                if (FlavorVisualCatalog.TryResolve(_flavorIds[i], out FlavorVisualDescriptor descriptor))
                {
                    names.Add(descriptor.DisplayName);
                }
            }

            int particles = _rig?.ActiveParticleSystemCount ?? 0;
            _status.text = $"{DishNames[_dishIndex]} · {string.Join(" + ", names)} · {names.Count} 种风味 · 气场粒子系统 {particles} / 7（每种最多 2 粒子）";
        }

        private Sprite LoadCurrentDish()
        {
            Sprite sprite = Resources.Load<Sprite>(DishPaths[_dishIndex]);
            if (sprite != null)
            {
                return sprite;
            }

            Sprite[] sprites = Resources.LoadAll<Sprite>(DishPaths[_dishIndex]);
            return sprites != null && sprites.Length > 0 ? sprites[0] : null;
        }

        private void ReleaseRig()
        {
            if (_rig != null)
            {
                _rig.Dispose();
                _rig = null;
            }

            for (int i = 0; i < _previews.Length; i++)
            {
                if (_previews[i] != null)
                {
                    _previews[i].texture = null;
                }
            }
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            SetRect(rect, anchorMin, anchorMax);
            return rect;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static TMP_Text CreateText(string name, Transform parent, string value, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.text = value;
            text.font = Resources.Load<TMP_FontAsset>("Fonts/AlimamaShuHeiTi-Bold SDF") ?? TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Ink;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = ButtonFill;
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(2f, -2f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            TMP_Text text = CreateText("Label", go.transform, label, 22f, TextAlignmentOptions.Center);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one);
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            return button;
        }
    }
}
