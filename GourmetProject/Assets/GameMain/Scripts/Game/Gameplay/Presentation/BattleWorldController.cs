using System;
using System.Collections;
using System.Collections.Generic;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using UnityEngine;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>
    /// 战斗内场景表现根控制器：棋盘、后厨、手牌区与拖拽放置。
    /// 现以场景内组件存在：背景/棋盘根/各锚点/分数文本/固定按钮均在 Battle.unity 摆好并通过 SerializeField 注入，
    /// 运行时只生成数据驱动内容（棋盘格随胃尺寸、菜品、道具槽、结算特效）。
    /// </summary>
    public sealed class BattleWorldController : MonoBehaviour
    {
        public const float Gap = 0f;
        private const float MaxCellSize = 1.2f;
        private const float MinCellSize = 0.42f;
        private const int PassiveSlotCapacity = 10;
        private const int PassiveSlotColumns = 2;
        private const int ActiveSlotCapacity = 2;

        // 回退视口半宽/半高（16:9 参考：orthographicSize 5.4）。
        private const float FallbackHalfW = 9.6f;
        private const float FallbackHalfH = 5.4f;

        // —— 场景内摆好的静态引用 ——
        [Header("Scene Refs")]
        [SerializeField] private Camera _camera;
        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private BoardView _boardView;
        [SerializeField] private Transform _piecesRoot;
        [SerializeField] private Transform _fxRoot;
        [SerializeField] private Transform _passiveItemsRoot;
        [SerializeField] private Transform _activeItemsRoot;
        [SerializeField] private TextMesh _scoreText;
        [SerializeField] private TextMesh _messageText;
        [SerializeField] private TextMesh _itemsText;
        [SerializeField] private WorldButtonView _overviewButton;
        [SerializeField] private WorldButtonView _eatButton;
        [SerializeField] private WorldButtonView[] _recipeButtons;
        [SerializeField] private SettlementSequencer _sequencer;
        [SerializeField] private BattleDoodleController _doodle;
        [SerializeField] private WorldButtonView _clearDoodleButton;
        [SerializeField] private WorldButtonView _toggleDoodleButton;

        // —— 运行时实例化用的 prefab ——
        [Header("Prefabs")]
        [SerializeField] private BoardCellView _boardCellPrefab;
        [SerializeField] private DishPieceView _dishPiecePrefab;
        [SerializeField] private WorldItemSlotView _itemSlotPrefab;
        [SerializeField] private MenuBookWorldView _menuBookPrefab;
        [SerializeField] private ServeHandView _serveHandPrefab;

        // 按棋盘尺寸自适应的单格世界尺寸与棋盘中心，BuildBoard 中计算。
        private float _cellSize = MaxCellSize;
        private Vector3 _boardCenter = new Vector3(0f, 0.15f, 0f);
        private float _halfW = FallbackHalfW;
        private float _halfH = FallbackHalfH;

        private readonly DishSpriteProvider _spriteProvider = new DishSpriteProvider();
        private readonly List<WorldItemSlotView> _passiveItemSlots = new List<WorldItemSlotView>();
        private readonly List<WorldItemSlotView> _activeItemSlots = new List<WorldItemSlotView>();
        private readonly List<MenuBookWorldView> _recipeBooks = new List<MenuBookWorldView>();
        private readonly List<DishPieceView> _placedPieces = new List<DishPieceView>();
        private readonly Dictionary<int, DishPieceView> _dishViewsById = new Dictionary<int, DishPieceView>();

        private GameRun _run;
        private BattleSession _session;
        private bool _settling;
        private bool _serving;

        private Action<string> _messageSink;
        private Action _eatClicked;
        private Action _overviewClicked;
        private Action<string> _activeItemClicked;
        private Action<DishInstance> _dishClicked;
        private Action _stateChanged;

        /// <summary>当前已加载战斗场景里的控制器实例（由战斗 UI/流程取用）。</summary>
        public static BattleWorldController Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Initialize(
            GameRun run,
            BattleSession session,
            Action<string> messageSink,
            Action stateChanged,
            Action eatClicked,
            Action overviewClicked,
            Action<string> activeItemClicked,
            Action<DishInstance> dishClicked)
        {
            _run = run;
            _session = session;
            _messageSink = messageSink;
            _stateChanged = stateChanged;
            _eatClicked = eatClicked;
            _overviewClicked = overviewClicked;
            _activeItemClicked = activeItemClicked;
            _dishClicked = dishClicked;
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            gameObject.SetActive(true);
            StopAllCoroutines();
            _settling = false;
            ComputeViewport();
            FitBackground();
            BuildBoard(session.Board);
            EnsureSequencer();
            EnsureItemRoots();
            ConfigureFixedButtons();
            BuildRecipeBooks();
            RebuildPlacedPieces();
            ConfigureDoodleHud();
            RefreshAll();
        }

        public void HideWorld()
        {
            gameObject.SetActive(false);
        }

        public void RefreshAll()
        {
            if (_session == null)
            {
                return;
            }

            _boardView.Sync();
            RefreshRecipes();
            RefreshScore();
            RefreshItems();
            RefreshActiveItems();
        }

        public void SyncBoardFromSession()
        {
            if (_session == null)
            {
                return;
            }

            RebuildPlacedPieces();
            RefreshAll();
        }

        public void TryServeDish(int slotIndex)
        {
            if (_session == null || _session.IsSettled || _settling || _serving)
            {
                return;
            }

            ServeResult result = _session.Serve(slotIndex);
            if (!result.Success)
            {
                SetMessage(ServeMessage(result.Outcome, slotIndex));
                RefreshAll();
                return;
            }

            DishPieceView placed = CreatePlacedPiece(result.Dish);
            Vector3 target = _boardView.Mapper.CellCenter(result.Dish.Placement.Origin);
            StartCoroutine(AnimateServe(placed, target));
            _boardView.Sync();
            SetMessage($"上菜：{result.Dish.Def.Name}");
            _stateChanged?.Invoke();
        }

        /// <summary>
        /// 商人手上菜：一只摊开手掌的手托着菜品从屏幕上方垂直降到目标格上方（悬停），
        /// 随后手向上抽离屏幕、菜品脱手落到格子并落定。手与菜全程带假阴影。
        /// </summary>
        private IEnumerator AnimateServe(DishPieceView piece, Vector3 target)
        {
            _serving = true;

            // 手世界高度：约 4 格高，受半屏高约束，保证起点能完全藏到屏幕上方外。
            float handHeight = Mathf.Clamp(_cellSize * 4.2f, 2.4f, _halfH * 1.5f);

            ServeHandView hand = null;
            if (_serveHandPrefab != null)
            {
                hand = Instantiate(_serveHandPrefab, transform);
                hand.gameObject.name = "ServeHand";
                hand.Build(_camera, handHeight);
            }

            // 悬停点：目标格正上方一点（菜品在此脱手）。起点：屏幕上方外。
            // 注意菜品 transform 锚在「原点格」，真正的图在脚印中心；这里一律按「图的中心」对位，
            // 才能让掌心始终托在食品正中央（多格菜也居中），落下时也不会横向漂移。
            Vector3 footCenter = FootprintCenterOffset(piece);
            Vector3 centerWorld = target + footCenter; // 落定后食品图的世界中心
            float hoverGap = Mathf.Max(0.3f, _cellSize * 0.6f);
            Vector3 hover = centerWorld + new Vector3(0f, hoverGap, 0f);
            Vector3 topPalm = new Vector3(centerWorld.x, _halfH + handHeight, 0f);

            // 飞行途中切到 PiecesFlying 层 + 举高悬浮，确保压在已摆放食品之上并带高度感。
            piece.SetFlying(true);
            piece.SetLift(1f);
            PlacePieceAtPalm(piece, hand, topPalm);

            // —— 阶段1：手托菜垂直下降到悬停点 ——
            const float descend = 0.34f;
            float t = 0f;
            while (t < descend && piece != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / descend);
                float eased = 1f - Mathf.Pow(1f - k, 3f); // 缓出
                Vector3 palm = Vector3.Lerp(topPalm, hover, eased);
                if (hand != null)
                {
                    hand.SetHeight(1f - eased * 0.8f); // 高空 1 → 贴近 0.2
                }

                PlacePieceAtPalm(piece, hand, palm);
                yield return null;
            }

            if (piece == null)
            {
                if (hand != null)
                {
                    Destroy(hand.gameObject);
                }

                _serving = false;
                yield break;
            }

            PlacePieceAtPalm(piece, hand, hover);

            // —— 阶段2：手向上抽离屏幕 + 菜品脱手落到格子 ——
            Vector3 pieceStart = piece.transform.position;
            const float drop = 0.26f;
            t = 0f;
            while (t < drop && piece != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / drop);

                // 菜品落下：缓出，lift 从举高收回贴桌。
                float pe = 1f - Mathf.Pow(1f - k, 3f);
                piece.transform.position = Vector3.Lerp(pieceStart, target, pe);
                piece.SetLift(1f - k);

                // 手抽走：缓入回到屏幕上方外，阴影淡出。
                if (hand != null)
                {
                    float he = k * k;
                    hand.SetPalmWorld(Vector3.Lerp(hover, topPalm, he));
                    hand.SetHeight(0.2f + 0.8f * he);
                }

                yield return null;
            }

            if (hand != null)
            {
                Destroy(hand.gameObject);
            }

            if (piece != null)
            {
                piece.transform.position = target;
                piece.SetLift(0f);
                yield return PresentationTween.PunchScale(piece.transform, 1.12f, 0.16f);
                // 落定后切回 Pieces 层，回到与其它棋盘食品一致的渲染顺序。
                piece.SetFlying(false);
            }

            _serving = false;
        }

        /// <summary>把菜品「图的中心」对齐到手掌心（无手时直接对到 palm 世界点）。</summary>
        /// <remarks>菜品 transform 锚在原点格、图在脚印中心，故减去脚印中心偏移，让图正好落在掌心。</remarks>
        private void PlacePieceAtPalm(DishPieceView piece, ServeHandView hand, Vector3 palm)
        {
            if (piece == null)
            {
                return;
            }

            Vector3 anchor = palm;
            if (hand != null)
            {
                hand.SetPalmWorld(palm);
                anchor = hand.PalmWorldPosition;
            }

            piece.transform.position = anchor - FootprintCenterOffset(piece);
        }

        /// <summary>菜品「图的中心」相对其 transform（原点格）的偏移 = 脚印中心，按朝向后的占格尺寸算。</summary>
        private Vector3 FootprintCenterOffset(DishPieceView piece)
        {
            if (piece == null)
            {
                return Vector3.zero;
            }

            float pitch = _cellSize + Gap;
            int w = Mathf.Max(1, piece.CurrentShape.Width);
            int h = Mathf.Max(1, piece.CurrentShape.Height);
            return new Vector3((w - 1) * pitch * 0.5f, -(h - 1) * pitch * 0.5f, 0f);
        }

        private void ComputeViewport()
        {
            if (_camera != null && _camera.orthographic && _camera.orthographicSize > 0f)
            {
                _halfH = _camera.orthographicSize;
                _halfW = _camera.orthographicSize * _camera.aspect;
            }
            else
            {
                _halfH = FallbackHalfH;
                _halfW = FallbackHalfW;
            }
        }

        private void EnsureSequencer()
        {
            if (_sequencer == null)
            {
                _sequencer = GetComponent<SettlementSequencer>();
            }
        }

        private void EnsureItemRoots()
        {
            if (_passiveItemsRoot == null)
            {
                _passiveItemsRoot = EnsureChildRoot("PassiveItemsRoot");
            }

            if (_activeItemsRoot == null)
            {
                _activeItemsRoot = EnsureChildRoot("ActiveItemsRoot");
            }
        }

        private Transform EnsureChildRoot(string childName)
        {
            Transform child = transform.Find(childName);
            if (child != null)
            {
                return child;
            }

            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private void FitBackground()
        {
            if (_background == null)
            {
                return;
            }

            BattleSorting.Apply(_background, BattleSorting.Background);
            SpriteRenderStyle.ApplyUnlitMaterial(_background);

            float height = _camera != null && _camera.orthographic ? _camera.orthographicSize * 2f : 10.8f;
            float width = height * (_camera != null ? _camera.aspect : 16f / 9f);
            FitSpriteToCover(_background.transform, _background, width, height);
        }

        private void BuildBoard(GpBoard board)
        {
            // 中央可用区：左侧让出后厨栏、右侧让出道具栏、上让分数、下让吃!/消息。
            float boardLeft = -_halfW + 3.0f;
            float boardRight = _halfW - 2.8f;
            float boardTop = _halfH - 1.4f;
            float boardBottom = -_halfH + 1.7f;
            float availW = Mathf.Max(1f, boardRight - boardLeft);
            float availH = Mathf.Max(1f, boardTop - boardBottom);

            int w = Mathf.Max(1, board.Width);
            int h = Mathf.Max(1, board.Height);
            _cellSize = Mathf.Clamp(Mathf.Min(availW / w, availH / h), MinCellSize, MaxCellSize);
            _boardCenter = new Vector3((boardLeft + boardRight) * 0.5f, (boardTop + boardBottom) * 0.5f, 0f);

            _boardView.Build(board, _cellSize, Gap, _boardCenter, OnCellClicked, _boardCellPrefab);
        }

        private void ConfigureFixedButtons()
        {
            _overviewButton?.Configure(
                new Vector2(1.15f, 0.52f),
                "总览",
                new Color(0.38f, 0.31f, 0.26f, 1f),
                () => _overviewClicked?.Invoke());

            _eatButton?.Configure(
                new Vector2(1.55f, 0.72f),
                "吃!",
                new Color(0.95f, 0.35f, 0.12f, 1f),
                () => _eatClicked?.Invoke());

            if (_recipeButtons == null)
            {
                return;
            }

            // 世界菜谱按钮已由菜单书替代，隐藏场景里原有按钮以免重复展示。
            foreach (WorldButtonView button in _recipeButtons)
            {
                if (button != null)
                {
                    button.gameObject.SetActive(false);
                }
            }
        }

        private void BuildRecipeBooks()
        {
            foreach (MenuBookWorldView book in _recipeBooks)
            {
                if (book != null)
                {
                    Destroy(book.gameObject);
                }
            }

            _recipeBooks.Clear();
            if (_session == null)
            {
                return;
            }

            int count = _session.Slots.Count;
            if (count <= 0)
            {
                return;
            }

            // 左侧后厨区：以屏幕左缘为基准向右铺开各菜谱书，竖直均分。
            // sizeMul 放大书体（>1 会向右压到棋盘区，属空间取舍）。场景物体书按世界尺寸直接摆放（高=宽*aspect）。
            const float margin = 0.2f;
            const float gap = 0.3f;
            const float aspect = 0.66f;
            const float sizeMul = 2.0f;
            float areaLeft = -_halfW + margin;
            float baseW = Mathf.Max(0.5f, 3.0f - margin * 2f);
            float worldW = baseW * sizeMul;

            float bookH = worldW * aspect;
            float availH = 2f * _halfH - 2f * margin;
            float needH = count * bookH + (count - 1) * gap;
            if (needH > availH && needH > 0f)
            {
                float k = availH / needH;
                worldW *= k;
                bookH *= k;
                needH = availH;
            }

            if (_menuBookPrefab == null)
            {
                Debug.LogWarning("[BattleWorldController] 未配置 _menuBookPrefab，菜谱书无法生成。");
                return;
            }

            float centerX = areaLeft + worldW * 0.5f;
            float blockTop = needH * 0.5f;
            for (int i = 0; i < count; i++)
            {
                MenuBookWorldView book = Instantiate(_menuBookPrefab, transform);
                book.gameObject.name = $"MenuBook_{i}";
                book.Build(i, _camera, OnBellServe, worldW, bookH);
                float y = blockTop - bookH * 0.5f - i * (bookH + gap);
                book.transform.position = new Vector3(centerX, y, 0f);
                _recipeBooks.Add(book);
            }
        }

        private void OnBellServe(int slotIndex)
        {
            TryServeDish(slotIndex);
        }

        private void RebuildPlacedPieces()
        {
            foreach (DishPieceView piece in _placedPieces)
            {
                if (piece != null)
                {
                    Destroy(piece.gameObject);
                }
            }

            _placedPieces.Clear();
            _dishViewsById.Clear();
            if (_session == null)
            {
                return;
            }

            foreach (DishInstance dish in _session.Board.Dishes)
            {
                CreatePlacedPiece(dish);
            }
        }

        private DishPieceView CreatePlacedPiece(DishInstance dish)
        {
            DishPieceView piece;
            if (_dishPiecePrefab != null)
            {
                piece = Instantiate(_dishPiecePrefab, _piecesRoot);
            }
            else
            {
                var go = new GameObject("Dish");
                go.transform.SetParent(_piecesRoot, false);
                piece = go.AddComponent<DishPieceView>();
            }

            piece.gameObject.name = $"Dish_{dish.Id}_{dish.Def.Id}";
            piece.transform.position = _boardView.Mapper.CellCenter(dish.Placement.Origin);
            piece.BuildPlaced(dish, _spriteProvider.Get(dish.Def), _cellSize, _cellSize + Gap, _dishClicked);
            _placedPieces.Add(piece);
            _dishViewsById[dish.Id] = piece;
            return piece;
        }

        private void RefreshRecipes()
        {
            if (_session == null || _run == null)
            {
                return;
            }

            for (int i = 0; i < _recipeBooks.Count; i++)
            {
                MenuBookWorldView book = _recipeBooks[i];
                if (book == null)
                {
                    continue;
                }

                if (i >= _session.Slots.Count)
                {
                    book.SetData(System.Array.Empty<string>(), _run.Database);
                    book.SetTitle($"菜谱{i + 1}");
                    book.SetInteractable(false);
                    continue;
                }

                RecipeSlot slot = _session.Slots[i];
                book.SetData(slot.Remaining, _run.Database);
                book.SetTitle($"菜谱{i + 1} · 剩 {slot.Count}");
                book.SetInteractable(!_session.IsSettled && !slot.IsEmpty);
            }
        }

        private void RefreshScore()
        {
            if (_scoreText == null || _run == null || _session == null)
            {
                return;
            }

            int score = _session.IsSettled ? _session.LastResult.Total : _session.PreviewScore().Total;
            string weekLabel = _run.IsEndless ? $"无尽 {_run.WeekIndex - _run.TotalWeeks}" : $"第 {_run.WeekIndex}/{_run.TotalWeeks} 周";
            string limit = _session.MaxServes >= 0 ? $" · 上菜 {_session.ServesUsed}/{_session.MaxServes}" : string.Empty;
            _scoreText.text = $"{weekLabel}  {score}/{_session.RequiredScore}{limit}";
            _eatButton?.SetInteractable(!_session.IsSettled);
        }

        private void RefreshItems()
        {
            ClearItemSlots(_passiveItemSlots);
            if (_run == null || _passiveItemsRoot == null)
            {
                return;
            }

            if (_itemsText != null)
            {
                _itemsText.text = "被动道具";
            }

            var passiveStates = new List<RunItemState>();
            foreach (RunItemState state in _run.Items)
            {
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(state.ItemId);
                if (item != null && item.Kind == cfg.ItemKind.Passive && !state.IsEmpty)
                {
                    passiveStates.Add(state);
                }
            }

            const float slotSize = 0.56f;
            const float gapX = 0.16f;
            const float gapY = 0.14f;
            float rightX = _halfW - 0.55f;
            float topY = _halfH - 0.78f;
            int shown = Mathf.Min(PassiveSlotCapacity, passiveStates.Count);
            for (int i = 0; i < shown; i++)
            {
                WorldItemSlotView slot = InstantiateItemSlot(_passiveItemsRoot);
                slot.gameObject.name = $"PassiveItemSlot_{i}";
                int col = i % PassiveSlotColumns;
                int row = i / PassiveSlotColumns;
                float x = rightX - col * (slotSize + gapX);
                float y = topY - row * (slotSize + gapY);
                slot.transform.position = new Vector3(x, y, 0f);

                RunItemState state = passiveStates[i];
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(state.ItemId);
                string badge = state.Level > 1 ? $"Lv{state.Level}" : string.Empty;
                slot.Bind(
                    new Vector2(slotSize, slotSize),
                    LoadItemIcon(item),
                    ShortName(item.Name),
                    badge,
                    QualityColor(item.Quality),
                    true,
                    () => ShowItemMessage(item, state));
                _passiveItemSlots.Add(slot);
            }
        }

        private void RefreshActiveItems()
        {
            ClearItemSlots(_activeItemSlots);
            if (_run == null || _activeItemsRoot == null)
            {
                return;
            }

            var activeStates = new List<RunItemState>();
            foreach (RunItemState state in _run.Items)
            {
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(state.ItemId);
                if (item == null || item.Kind != cfg.ItemKind.Active || state.Count <= 0)
                {
                    continue;
                }

                activeStates.Add(state);
            }

            const float slotSize = 0.58f;
            const float gap = 0.34f;
            float startX = _halfW - 2.25f;
            float y = -_halfH + 0.55f;
            int shown = Mathf.Min(ActiveSlotCapacity, activeStates.Count);

            for (int i = 0; i < ActiveSlotCapacity; i++)
            {
                WorldItemSlotView slot = InstantiateItemSlot(_activeItemsRoot);
                slot.gameObject.name = $"ActiveItemSlot_{i}";
                slot.transform.position = new Vector3(startX + i * (slotSize + gap), y, 0f);

                if (i < shown)
                {
                    RunItemState state = activeStates[i];
                    cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(state.ItemId);
                    string captured = state.ItemId;
                    bool usableNow = _session != null && !_session.IsSettled && item.TriggerTiming == cfg.ItemTriggerTiming.BeforeEat;
                    string badge = state.Count > 1 ? $"x{state.Count}" : string.Empty;
                    slot.Bind(
                        new Vector2(slotSize, slotSize),
                        LoadItemIcon(item),
                        ShortName(item.Name),
                        badge,
                        QualityColor(item.Quality),
                        usableNow,
                        () => _activeItemClicked?.Invoke(captured));
                }
                else
                {
                    slot.Bind(new Vector2(slotSize, slotSize), null, string.Empty, string.Empty, Color.white, false, null);
                }

                _activeItemSlots.Add(slot);
            }
        }

        private void ClearItemSlots(List<WorldItemSlotView> slots)
        {
            foreach (WorldItemSlotView slot in slots)
            {
                if (slot != null)
                {
                    Destroy(slot.gameObject);
                }
            }

            slots.Clear();
        }

        private WorldItemSlotView InstantiateItemSlot(Transform root)
        {
            if (_itemSlotPrefab != null)
            {
                return Instantiate(_itemSlotPrefab, root);
            }

            var go = new GameObject("WorldItemSlot", typeof(BoxCollider2D));
            go.transform.SetParent(root, false);
            return go.AddComponent<WorldItemSlotView>();
        }

        private static Sprite LoadItemIcon(cfg.Item item)
        {
            if (item == null || string.IsNullOrEmpty(item.Icon))
            {
                return null;
            }

            return Resources.Load<Sprite>(item.Icon);
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return name.Length <= 2 ? name : name.Substring(0, 2);
        }

        private static Color QualityColor(cfg.ItemQuality quality)
        {
            switch (quality)
            {
                case cfg.ItemQuality.Uncommon:
                    return new Color(0.50f, 0.86f, 0.46f, 1f);
                case cfg.ItemQuality.Rare:
                    return new Color(0.35f, 0.62f, 1f, 1f);
                case cfg.ItemQuality.Epic:
                    return new Color(0.74f, 0.42f, 1f, 1f);
                case cfg.ItemQuality.Legendary:
                    return new Color(1f, 0.72f, 0.22f, 1f);
                default:
                    return new Color(0.92f, 0.86f, 0.74f, 1f);
            }
        }

        private void ShowItemMessage(cfg.Item item, RunItemState state)
        {
            if (item == null || state == null)
            {
                return;
            }

            string level = item.Kind == cfg.ItemKind.Passive && state.Level > 1 ? $" Lv.{state.Level}" : string.Empty;
            SetMessage($"{item.Name}{level}：{item.Desc}");
        }

        /// <summary>
        /// 配置右下角涂鸦 HUD（清空 / 显隐）：两个按钮在 Battle.unity 里预置好、通过 SerializeField 注入，
        /// 这里只喂外观/回调（位置来自场景），并在每次进入战斗时清空笔迹、复位为可见。
        /// </summary>
        private void ConfigureDoodleHud()
        {
            if (_doodle == null)
            {
                return;
            }

            var buttonSize = new Vector2(1.4f, 0.5f);

            _clearDoodleButton?.Configure(
                buttonSize,
                "清空涂鸦",
                new Color(0.62f, 0.4f, 0.32f, 1f),
                () => _doodle.Clear());

            _toggleDoodleButton?.Configure(
                buttonSize,
                "隐藏涂鸦",
                new Color(0.4f, 0.55f, 0.42f, 1f),
                ToggleDoodleVisible);

            _doodle.Clear();
            _doodle.SetVisible(true);
            _toggleDoodleButton?.SetLabel("隐藏涂鸦");
        }

        private void ToggleDoodleVisible()
        {
            if (_doodle == null)
            {
                return;
            }

            bool next = !_doodle.IsVisible;
            _doodle.SetVisible(next);
            if (_toggleDoodleButton != null)
            {
                _toggleDoodleButton.SetLabel(next ? "隐藏涂鸦" : "显示涂鸦");
            }
        }

        private void OnCellClicked(GridPos pos)
        {
            if (_session == null)
            {
                return;
            }

            DishInstance dish = _session.Board.DishAt(pos);
            if (dish != null)
            {
                _dishClicked?.Invoke(dish);
            }
        }

        private void SetMessage(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message;
            }

            _messageSink?.Invoke(message);
        }

        public void ShowMessage(string message)
        {
            SetMessage(message);
        }

        private string ServeMessage(ServeOutcome outcome, int slotIndex)
        {
            switch (outcome)
            {
                case ServeOutcome.SlotEmpty:
                    return $"菜谱{slotIndex + 1} 已空。";
                case ServeOutcome.NoFittingDish:
                    return "这本菜谱里没有能放下的菜了。";
                case ServeOutcome.LimitReached:
                    return $"限量供应：本局最多上 {_session.MaxServes} 道菜。";
                default:
                    return "现在不能上菜。";
            }
        }

        private static void FitSpriteToCover(Transform target, SpriteRenderer renderer, float width, float height)
        {
            if (renderer.sprite == null)
            {
                target.localScale = new Vector3(width, height, 1f);
                return;
            }

            Vector2 size = renderer.sprite.bounds.size;
            float scale = Mathf.Max(width / size.x, height / size.y);
            target.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>播放背包乱斗式逐菜结算演出，完成后回调上层决定过关/失败 UI。</summary>
        public void PlaySettlement(ScoreResult result, Action onComplete)
        {
            if (_sequencer == null || _session == null || result == null)
            {
                onComplete?.Invoke();
                return;
            }

            _settling = true;
            SetButtonsInteractable(false);
            StartCoroutine(SettlementRoutine(result, onComplete));
        }

        private IEnumerator SettlementRoutine(ScoreResult result, Action onComplete)
        {
            yield return _sequencer.Play(
                _session,
                result,
                _dishViewsById,
                _boardView.Mapper,
                _fxRoot,
                RenderSettlementScore,
                null);

            _settling = false;
            RefreshAll();
            onComplete?.Invoke();
        }

        private void RenderSettlementScore(int score)
        {
            if (_scoreText == null || _run == null || _session == null)
            {
                return;
            }

            string weekLabel = _run.IsEndless ? $"无尽 {_run.WeekIndex - _run.TotalWeeks}" : $"第 {_run.WeekIndex}/{_run.TotalWeeks} 周";
            string limit = _session.MaxServes >= 0 ? $" · 上菜 {_session.ServesUsed}/{_session.MaxServes}" : string.Empty;
            _scoreText.text = $"{weekLabel}  {score}/{_session.RequiredScore}{limit}";
        }

        private void SetButtonsInteractable(bool interactable)
        {
            _eatButton?.SetInteractable(interactable);
            if (_recipeButtons != null)
            {
                foreach (WorldButtonView button in _recipeButtons)
                {
                    button?.SetInteractable(interactable);
                }
            }

            foreach (WorldItemSlotView slot in _activeItemSlots)
            {
                slot?.SetInteractable(interactable);
            }

            foreach (MenuBookWorldView book in _recipeBooks)
            {
                book?.SetInteractable(interactable);
            }
        }
    }
}
