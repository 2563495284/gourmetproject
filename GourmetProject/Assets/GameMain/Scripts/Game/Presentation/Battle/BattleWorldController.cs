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
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 战斗内场景表现根控制器：棋盘、后厨、手牌区与拖拽放置。
    /// 现以场景内组件存在：背景/棋盘根/各锚点/分数文本/固定按钮均在 Battle.unity 摆好并通过 SerializeField 注入，
    /// 运行时只生成数据驱动内容（棋盘格随胃尺寸、菜品、道具槽、结算特效）。
    /// </summary>
    public sealed class BattleWorldController : MonoBehaviour
    {
        public const float Gap = BoardLayout.Gap;
        private const float MaxCellSize = BoardLayout.MaxCellSize;
        private const float MinCellSize = BoardLayout.MinCellSize;

        // 棋盘居中定位的底部边距：Food 态给底部菜谱抽屉让 2.7，编辑态还要给候选托盘条让到 3.6。
        private const float FoodBoardBottomMargin = 2.7f;
        private const float EditBoardBottomMargin = 3.6f;
        private const int PassiveSlotCapacity = 10;
        private const int PassiveSlotColumns = 2;
        private const int ActiveSlotCapacity = 2;

        // 回退视口半宽/半高（16:9 参考：orthographicSize 5.4）。
        private const float FallbackHalfW = 9.6f;
        private const float FallbackHalfH = 5.4f;

        // —— 场景内摆好的静态引用 ——
        [Header("Scene Refs")]
        [SerializeField] private Camera _camera;
        [SerializeField] private BoardView _boardView;
        [SerializeField] private Transform _piecesRoot;
        [SerializeField] private Transform _fxRoot;
        [SerializeField] private Transform _passiveItemsRoot;
        [SerializeField] private Transform _activeItemsRoot;
        [SerializeField] private TextMesh _scoreText;
        [SerializeField] private SettlementScoreFireView _scoreFire;
        [SerializeField] private TextMesh _messageText;
        [SerializeField] private TextMesh _itemsText;
        [SerializeField] private SettlementSequencer _sequencer;
        [SerializeField] private BattleDoodleController _doodle;

        // —— 运行时实例化用的 prefab ——
        [Header("Prefabs")]
        [SerializeField] private BoardCellView _boardCellPrefab;
        [SerializeField] private DishPieceView _dishPiecePrefab;
        [SerializeField] private WorldItemSlotView _itemSlotPrefab;
        [SerializeField] private MenuBookWorldView _menuBookPrefab;
        [SerializeField] private ServeHandView _serveHandPrefab;
        [Tooltip("食物调整态的世界按钮（X/勾/撤销）prefab，复用 Prefabs/Battle/WorldButton。")]
        [SerializeField] private WorldButtonView _worldButtonPrefab;

        [Header("上菜动画")]
        [SerializeField] private float _serveCarryScale = 1.22f;
        [SerializeField] private float _serveDescendDuration = 0.34f;
        [SerializeField] private float _serveWithdrawDuration = 0.18f;
        [SerializeField] private float _serveDropDuration = 0.24f;

        // 棋盘锁定在该屏幕矩形内 fit 并居中（由 BattleForm 传入的 HUD 空区 BoardArea）；为空则回落视口边距布局。
        private const float BoardAreaMinCellSize = 0.12f;
        private RectTransform _boardArea;

        // 按棋盘尺寸自适应的单格世界尺寸与棋盘中心，BuildBoard 中计算。
        private float _cellSize = MaxCellSize;
        private Vector3 _boardCenter = new Vector3(0f, 0.15f, 0f);
        private float _halfW = FallbackHalfW;
        private float _halfH = FallbackHalfH;

        private ServeAnimator _serveAnimator;

        // 棋盘编辑 / 只读胃视图的表现与交互拆到协作组件；本类只做 Food 态与世界互斥态调度（外壳）。
        private BoardEditController _boardEdit;

        // 食物调整（局内删除/移动菜品）交互拆到协作组件，仅 Food 态启用。
        private FoodAdjustController _foodAdjust;

        private readonly DishSpriteProvider _spriteProvider = new DishSpriteProvider();
        private readonly List<WorldItemSlotView> _passiveItemSlots = new List<WorldItemSlotView>();
        private readonly List<WorldItemSlotView> _activeItemSlots = new List<WorldItemSlotView>();
        private readonly List<MenuBookWorldView> _recipeBooks = new List<MenuBookWorldView>();
        private readonly List<DishPieceView> _placedPieces = new List<DishPieceView>();
        private readonly Dictionary<int, DishPieceView> _dishViewsById = new Dictionary<int, DishPieceView>();

        private enum WorldMode
        {
            Hidden,
            Food,
            BoardEdit,
            StomachView,
        }

        private GameRun _run;
        private BattleSession _session;
        private bool _settling;
        private bool _serving;
        private WorldMode _worldMode = WorldMode.Hidden;

        private Action<string> _messageSink;
        private Action<string> _activeItemClicked;
        private Action<DishInstance> _dishClicked;
        private Action _stateChanged;

        /// <summary>当前已加载战斗场景里的控制器实例（由战斗 UI/流程取用）。</summary>
        public static BattleWorldController Instance { get; private set; }

        public bool CanEnterStomachView
            => _worldMode != WorldMode.StomachView
                && (_worldMode != WorldMode.Food || (!_settling && !_serving));

        private void Awake()
        {
            Instance = this;

            EnsureBoardEdit();

            // 默认非美食态：世界棋盘与其专属按钮（总览/吃/涂鸦）默认隐藏，只有 StartBattle→Initialize 才显示。
            // 场景里 BattleSceneRoot 默认 active，若不在此处收起，行动选择等非美食态一进场景就会露出这堆美食专属按钮。
            HideWorld();
        }

        /// <summary>确保棋盘编辑协作组件存在并注入共享场景引用（运行时挂到同一战斗场景根上）。</summary>
        private void EnsureBoardEdit()
        {
            if (_boardEdit == null)
            {
                _boardEdit = GetComponent<BoardEditController>();
                if (_boardEdit == null)
                {
                    _boardEdit = gameObject.AddComponent<BoardEditController>();
                }
            }

            _boardEdit.Configure(this, _boardView, _boardCellPrefab, _piecesRoot, _camera);
        }

        /// <summary>供 <see cref="BoardEditController"/> 在编辑/胃视图结束时通知外壳复位世界互斥态。</summary>
        internal void ClearBoardMode()
        {
            if (_worldMode == WorldMode.BoardEdit || _worldMode == WorldMode.StomachView)
            {
                _worldMode = WorldMode.Hidden;
            }
        }

        /// <summary>是否正处于可拖拽的棋盘编辑态。</summary>
        public bool IsEditingBoard => _boardEdit != null && _boardEdit.IsEditing;

        private void EnsureFoodAdjust()
        {
            if (_foodAdjust == null)
            {
                _foodAdjust = GetComponent<FoodAdjustController>();
                if (_foodAdjust == null)
                {
                    _foodAdjust = gameObject.AddComponent<FoodAdjustController>();
                }
            }

            _foodAdjust.Configure(this);
        }

        /// <summary>是否正处于食物调整态。</summary>
        public bool IsFoodAdjusting => _foodAdjust != null && _foodAdjust.IsActive;

        /// <summary>进入食物调整态（仅 Food 态、未结算时可用）。<paramref name="onExited"/> 供确认提交后自动退出时回通知壳更新 UI。</summary>
        public void BeginFoodAdjust(Action onExited)
        {
            if (_worldMode != WorldMode.Food || _session == null || _session.IsSettled)
            {
                return;
            }

            EnsureFoodAdjust();
            _foodAdjust.Begin(onExited);
            SetPlacedPiecesClickEnabled(false);
        }

        /// <summary>退出食物调整态（用户点「返回」或提交后调用）。会取消未提交的移动并复位表现。</summary>
        public void EndFoodAdjust()
        {
            if (_foodAdjust == null || !_foodAdjust.IsActive)
            {
                return;
            }

            _foodAdjust.End();
            RebuildPlacedPieces();
            SetPlacedPiecesClickEnabled(true);
        }

        private void SetPlacedPiecesClickEnabled(bool enabled)
        {
            foreach (DishPieceView piece in _placedPieces)
            {
                piece?.SetClickEnabled(enabled);
            }
        }

        // —— 供 FoodAdjustController 读取的内部引用 ——
        internal GpBoard AdjustBoard => _session?.Board;
        internal BoardView AdjustBoardView => _boardView;
        internal Transform AdjustPiecesRoot => _piecesRoot;
        internal Camera AdjustCamera => _camera;
        internal float AdjustCellSize => _cellSize;
        internal DishPieceView AdjustDishPiecePrefab => _dishPiecePrefab;
        internal WorldButtonView AdjustWorldButtonPrefab => _worldButtonPrefab;
        internal DishSpriteProvider AdjustSpriteProvider => _spriteProvider;
        internal GameRun AdjustRun => _run;

        internal DishPieceView GetPieceView(int id)
        {
            return _dishViewsById.TryGetValue(id, out DishPieceView view) ? view : null;
        }

        /// <summary>食物调整删除/撤销后重建棋盘菜品表现，并保持调整态下的点击屏蔽。</summary>
        internal void RebuildAfterAdjust()
        {
            RebuildPlacedPieces();
            RefreshAll();
            if (IsFoodAdjusting)
            {
                SetPlacedPiecesClickEnabled(false);
            }
        }

        /// <summary>
        /// 进入棋盘编辑页：外壳先收起 Food 态表现并切到编辑互斥态，再把编辑页构建交给协作组件。
        /// </summary>
        public void BeginBoardEdit(GameRun run, IReadOnlyList<string> candidateIds, Action<bool> onDone)
        {
            if (run == null)
            {
                onDone?.Invoke(false);
                return;
            }

            EnsureBoardEdit();
            EndStomachView();

            gameObject.SetActive(true);
            StopAllCoroutines();
            _worldMode = WorldMode.BoardEdit;
            _settling = false;
            _serving = false;
            _session = null;
            SetFoodWorldElementsVisible(false);
            ClearPlacedPieces();

            _boardEdit.BeginBoardEdit(run, candidateIds, onDone);
        }

        /// <summary>进入只读胃视图：外壳收起 Food 态并切到胃视图互斥态，交由协作组件复用棋盘布局渲染。</summary>
        public void BeginStomachView(GameRun run)
        {
            if (run == null || !CanEnterStomachView)
            {
                return;
            }

            EnsureBoardEdit();
            if (_boardEdit.IsEditing)
            {
                _boardEdit.EndBoardEdit();
            }

            _run = run;
            _session = null;
            gameObject.SetActive(true);
            StopAllCoroutines();
            _worldMode = WorldMode.StomachView;
            _settling = false;
            _serving = false;
            SetFoodWorldElementsVisible(false);
            HideWorldPanels();
            ClearPlacedPieces();

            _boardEdit.BeginStomachView(run);
        }

        public void EndStomachView()
        {
            if (_worldMode == WorldMode.StomachView)
            {
                _worldMode = WorldMode.Hidden;
            }

            _boardEdit?.EndStomachView();
        }

        public void SkipBoardEditPack()
        {
            if (_worldMode != WorldMode.BoardEdit || _boardEdit == null || !_boardEdit.IsEditing)
            {
                return;
            }

            _boardEdit.SkipBoardEditPack();
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
            Action<string> activeItemClicked,
            Action<DishInstance> dishClicked,
            bool resetDoodle = true)
        {
            _run = run;
            _session = session;
            _messageSink = messageSink;
            _stateChanged = stateChanged;
            _activeItemClicked = activeItemClicked;
            _dishClicked = dishClicked;
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            EnsureServeAnimator();
            EnsureFoodAdjust();
            if (_foodAdjust != null && _foodAdjust.IsActive)
            {
                _foodAdjust.End();
            }

            gameObject.SetActive(true);
            StopAllCoroutines();
            _worldMode = WorldMode.Food;
            _settling = false;
            ComputeViewport();
            BuildBoard(session.Board);
            EnsureSequencer();
            EnsureScoreFire();
            // 道具（被动/主动）与菜谱面板已迁到常驻屏幕空间 HUD（BattleForm），世界空间不再渲染这些面板；
            // 世界空间只保留棋盘、菜品、上菜/结算演出与涂鸦表现。
            HideWorldPanels();
            SetFoodWorldElementsVisible(true);
            RebuildPlacedPieces();
            if (resetDoodle)
            {
                ResetDoodle();
            }
            else
            {
                _doodle?.SetVisible(true);
            }
            RefreshAll();
        }

        public void HideWorld()
        {
            if (_foodAdjust != null && _foodAdjust.IsActive)
            {
                _foodAdjust.End();
            }

            if (_boardEdit != null && _boardEdit.IsEditing)
            {
                _boardEdit.EndBoardEdit();
            }

            EndStomachView();
            SetFoodWorldElementsVisible(false);
            _worldMode = WorldMode.Hidden;
            gameObject.SetActive(false);
        }

        /// <summary>战斗结束后清理本场运行时棋盘表现，避免已摆菜品残留到后续非战斗状态。</summary>
        public void ClearBattleBoard()
        {
            StopAllCoroutines();
            _settling = false;
            _serving = false;
            _session = null;
            ClearPlacedPieces();
            _scoreFire?.Hide();
            _doodle?.Clear();
        }

        private void SetFoodWorldElementsVisible(bool visible)
        {
            if (_scoreText != null)
            {
                _scoreText.gameObject.SetActive(visible);
            }

            if (_messageText != null)
            {
                _messageText.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                _scoreFire?.Hide();
                _doodle?.SetVisible(false);
            }
        }

        public void RefreshAll()
        {
            if (_session == null)
            {
                return;
            }

            _boardView.Sync();
            RefreshScore();
        }

        /// <summary>隐藏迁到 HUD 的世界空间面板：被动/主动道具槽、道具标题、菜谱书。</summary>
        private void HideWorldPanels()
        {
            ClearItemSlots(_passiveItemSlots);
            ClearItemSlots(_activeItemSlots);

            foreach (MenuBookWorldView book in _recipeBooks)
            {
                if (book != null)
                {
                    Destroy(book.gameObject);
                }
            }

            _recipeBooks.Clear();

            if (_itemsText != null)
            {
                _itemsText.gameObject.SetActive(false);
            }

            if (_passiveItemsRoot != null)
            {
                _passiveItemsRoot.gameObject.SetActive(false);
            }

            if (_activeItemsRoot != null)
            {
                _activeItemsRoot.gameObject.SetActive(false);
            }
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
            _serving = true;
            EnsureServeAnimator();
            StartCoroutine(_serveAnimator.Animate(placed, target, _camera, _cellSize, _halfH, FinishServing));
            _boardView.Sync();
            SetMessage($"上菜：{result.Dish.Def.Name}");
            _stateChanged?.Invoke();
        }

        private void EnsureServeAnimator()
        {
            _serveAnimator ??= new ServeAnimator(
                transform,
                _serveHandPrefab,
                new ServeAnimator.Config
                {
                    CarryScale = _serveCarryScale,
                    DescendDuration = _serveDescendDuration,
                    WithdrawDuration = _serveWithdrawDuration,
                    DropDuration = _serveDropDuration,
                });
        }

        private void FinishServing()
        {
            _serving = false;
            _stateChanged?.Invoke();
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

        /// <summary>
        /// 由 BattleForm 传入 HUD 里的空区矩形，棋盘将始终 fit 并居中锁定在该屏幕区域内（超大胃继续缩放显示）。
        /// 传 null 回落到按视口边距布局。
        /// </summary>
        public void SetBoardArea(RectTransform area)
        {
            _boardArea = area;
        }

        private void BuildBoard(GpBoard board)
        {
            // 中央可用区：菜谱/道具面板已迁到常驻 HUD（左右栏 + 底部菜谱抽屉），棋盘居中在中部内容区，
            // 由 BoardLayout 统一按胃包围盒铺满可用区并居中（与编辑/胃视图态共用同一套定位算法）。
            BoardPlacement placement = TryComputeBoardAreaRect(out float left, out float right, out float bottom, out float top)
                ? BoardLayout.ComputeInRect(left, right, bottom, top, board, BoardAreaMinCellSize)
                : BoardLayout.Compute(_halfW, _halfH, board, FoodBoardBottomMargin);
            _cellSize = placement.CellSize;
            _boardCenter = placement.Position;

            // 局部空间：棋盘以 BoardView.transform 为局部帧（BoardRoot），世界摆放/居中由其 transform 决定。
            // 保持 scale 恒等、rotation 恒等，避免子级格子/菜品被二次缩放或旋转。
            _boardView.transform.rotation = Quaternion.identity;
            _boardView.transform.localScale = Vector3.one;
            _boardView.transform.position = _boardCenter;

            // 菜品根挂到 BoardRoot 下，使已放置菜品与棋盘共享同一局部帧（棋盘整体移动/缩放时随动）。
            if (_piecesRoot != null && _piecesRoot.parent != _boardView.transform)
            {
                _piecesRoot.SetParent(_boardView.transform, worldPositionStays: false);
                _piecesRoot.localPosition = Vector3.zero;
                _piecesRoot.localRotation = Quaternion.identity;
                _piecesRoot.localScale = Vector3.one;
            }

            _boardView.Build(board, _cellSize, Gap, OnCellClicked, _boardCellPrefab);
        }

        /// <summary>把 HUD 里的 BoardArea 矩形四角投影到 BattleCamera 世界平面(z=0)，得到棋盘可用区的世界矩形边界。</summary>
        private bool TryComputeBoardAreaRect(out float left, out float right, out float bottom, out float top)
        {
            left = right = bottom = top = 0f;
            if (_boardArea == null || _camera == null)
            {
                return false;
            }

            Canvas canvas = _boardArea.GetComponentInParent<Canvas>();
            Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            var corners = new Vector3[4];
            _boardArea.GetWorldCorners(corners);

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            float depth = -_camera.transform.position.z;
            for (int i = 0; i < 4; i++)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCam, corners[i]);
                Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
                minX = Mathf.Min(minX, world.x);
                maxX = Mathf.Max(maxX, world.x);
                minY = Mathf.Min(minY, world.y);
                maxY = Mathf.Max(maxY, world.y);
            }

            left = minX;
            right = maxX;
            bottom = minY;
            top = maxY;
            return maxX > minX && maxY > minY;
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
            ClearPlacedPieces();
            if (_session == null)
            {
                return;
            }

            foreach (DishInstance dish in _session.Board.Dishes)
            {
                CreatePlacedPiece(dish);
            }
        }

        private void ClearPlacedPieces()
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
            // 菜品挂在 BoardRoot 下，用局部坐标贴格（与棋盘共享局部帧）。
            piece.transform.localPosition = _boardView.Mapper.CellCenterLocal(dish.Placement.Origin);
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
                if (item != null && item.Kind == cfg.ItemKind.Passive)
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

            // 主动道具多实例制：同一 id 可能有多条，这里按 id 聚合成一个槽，份数用角标 xN 展示。
            var activeStates = new List<RunItemState>();
            var activeCounts = new Dictionary<string, int>();
            foreach (RunItemState state in _run.Items)
            {
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(state.ItemId);
                if (item == null || item.Kind != cfg.ItemKind.Active)
                {
                    continue;
                }

                if (activeCounts.TryGetValue(state.ItemId, out int held))
                {
                    activeCounts[state.ItemId] = held + 1;
                }
                else
                {
                    activeCounts[state.ItemId] = 1;
                    activeStates.Add(state);
                }
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
                    int held = activeCounts[state.ItemId];
                    string badge = held > 1 ? $"x{held}" : string.Empty;
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

        public string DoodleToggleLabel => _doodle != null && _doodle.IsVisible ? "隐藏涂鸦" : "显示涂鸦";

        /// <summary>每次进入战斗时清空笔迹，并把涂鸦层复位为可见。</summary>
        public void ResetDoodle()
        {
            if (_doodle == null)
            {
                return;
            }

            _doodle.Clear();
            _doodle.SetVisible(true);
        }

        public void ClearDoodle()
        {
            _doodle?.Clear();
        }

        public bool ToggleDoodleVisible()
        {
            if (_doodle == null)
            {
                return false;
            }

            bool next = !_doodle.IsVisible;
            _doodle.SetVisible(next);
            return next;
        }

        private void OnCellClicked(GridPos pos)
        {
            if (_session == null || IsFoodAdjusting)
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
                _scoreFire,
                RenderSettlementScore,
                null);

            _settling = false;
            RefreshAll();
            onComplete?.Invoke();
        }

        private void EnsureScoreFire()
        {
            if (_scoreFire == null && _scoreText != null)
            {
                _scoreFire = _scoreText.GetComponentInChildren<SettlementScoreFireView>(true);
            }

            if (_scoreFire == null)
            {
                Transform parent = _scoreText != null ? _scoreText.transform : _fxRoot;
                if (parent != null)
                {
                    var go = new GameObject("SettlementScoreFire");
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = _scoreText != null ? new Vector3(1.15f, -0.04f, -0.02f) : Vector3.zero;
                    _scoreFire = go.AddComponent<SettlementScoreFireView>();
                }
            }

            _scoreFire?.Hide();
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
