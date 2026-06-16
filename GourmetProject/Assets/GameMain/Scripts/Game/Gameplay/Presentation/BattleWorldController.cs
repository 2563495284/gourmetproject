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
    /// 运行时只生成数据驱动内容（棋盘格随胃尺寸、菜品、主动道具按钮、结算特效）。
    /// </summary>
    public sealed class BattleWorldController : MonoBehaviour
    {
        public const float Gap = 0f;
        private const float MaxCellSize = 1.2f;
        private const float MinCellSize = 0.42f;

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
        [SerializeField] private Transform _activeItemsRoot;
        [SerializeField] private TextMesh _scoreText;
        [SerializeField] private TextMesh _messageText;
        [SerializeField] private TextMesh _itemsText;
        [SerializeField] private WorldButtonView _overviewButton;
        [SerializeField] private WorldButtonView _eatButton;
        [SerializeField] private WorldButtonView[] _recipeButtons;
        [SerializeField] private SettlementSequencer _sequencer;

        // —— 运行时实例化用的 prefab ——
        [Header("Prefabs")]
        [SerializeField] private BoardCellView _boardCellPrefab;
        [SerializeField] private DishPieceView _dishPiecePrefab;
        [SerializeField] private WorldButtonView _worldButtonPrefab;

        // 按棋盘尺寸自适应的单格世界尺寸与棋盘中心，BuildBoard 中计算。
        private float _cellSize = MaxCellSize;
        private Vector3 _boardCenter = new Vector3(0f, 0.15f, 0f);
        private float _halfW = FallbackHalfW;
        private float _halfH = FallbackHalfH;

        private readonly DishSpriteProvider _spriteProvider = new DishSpriteProvider();
        private readonly List<WorldButtonView> _activeButtons = new List<WorldButtonView>();
        private readonly List<DishPieceView> _placedPieces = new List<DishPieceView>();
        private readonly Dictionary<int, DishPieceView> _dishViewsById = new Dictionary<int, DishPieceView>();

        private GameRun _run;
        private BattleSession _session;
        private bool _settling;

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
            ConfigureFixedButtons();
            RebuildPlacedPieces();
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
            if (_session == null || _session.IsSettled || _settling)
            {
                return;
            }

            Vector3 start = _recipeButtons != null && slotIndex >= 0 && slotIndex < _recipeButtons.Length
                ? _recipeButtons[slotIndex].transform.position
                : new Vector3(-4.2f, 2f, 0f);

            ServeResult result = _session.Serve(slotIndex);
            if (!result.Success)
            {
                SetMessage(ServeMessage(result.Outcome, slotIndex));
                RefreshAll();
                return;
            }

            DishPieceView placed = CreatePlacedPiece(result.Dish);
            placed.transform.position = start;
            // 飞行途中切到 PiecesFlying 层 + 举高悬浮，确保压在已摆放的棋盘食品之上并带高度感。
            placed.SetFlying(true);
            placed.SetLift(1f);
            StartCoroutine(AnimateServe(placed, _boardView.Mapper.CellCenter(result.Dish.Placement.Origin)));
            _boardView.Sync();
            SetMessage($"上菜：{result.Dish.Def.Name}");
            _stateChanged?.Invoke();
        }

        private IEnumerator AnimateServe(DishPieceView piece, Vector3 target)
        {
            // 飞行途中保持举高（阴影远、大、淡）。
            yield return PresentationTween.MoveTo(piece != null ? piece.transform : null, target, 0.26f);
            if (piece == null)
            {
                yield break;
            }

            // 落定：阴影从举高收回贴桌，形成「啪」地放下的接触感。
            float t = 0f;
            const float landDuration = 0.14f;
            while (t < landDuration && piece != null)
            {
                t += Time.deltaTime;
                piece.SetLift(1f - Mathf.Clamp01(t / landDuration));
                yield return null;
            }

            if (piece == null)
            {
                yield break;
            }

            piece.SetLift(0f);
            yield return PresentationTween.PunchScale(piece.transform, 1.12f, 0.16f);
            // 落定后切回 Pieces 层，回到与其它棋盘食品一致的渲染顺序。
            piece.SetFlying(false);
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

            for (int i = 0; i < _recipeButtons.Length; i++)
            {
                int index = i;
                _recipeButtons[i]?.Configure(
                    new Vector2(1.7f, 0.78f),
                    $"菜谱{i + 1}",
                    new Color(0.82f, 0.48f, 0.18f, 1f),
                    () => TryServeDish(index));
            }
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
            if (_recipeButtons == null)
            {
                return;
            }

            for (int i = 0; i < _recipeButtons.Length; i++)
            {
                if (_recipeButtons[i] == null)
                {
                    continue;
                }

                if (_session == null || i >= _session.Slots.Count)
                {
                    _recipeButtons[i].SetLabel($"菜谱{i + 1}");
                    _recipeButtons[i].SetInteractable(false);
                    continue;
                }

                RecipeSlot slot = _session.Slots[i];
                _recipeButtons[i].SetLabel($"菜谱{i + 1}\n剩 {slot.Count}");
                _recipeButtons[i].SetInteractable(!_session.IsSettled && !slot.IsEmpty);
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
            if (_itemsText == null || _run == null)
            {
                return;
            }

            var sb = new System.Text.StringBuilder("被动道具\n");
            bool any = false;
            foreach (string itemId in _run.ItemIds)
            {
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(itemId);
                if (item != null && item.Kind == cfg.ItemKind.Passive)
                {
                    sb.AppendLine(item.Name);
                    any = true;
                }
            }

            _itemsText.text = any ? sb.ToString() : "被动道具\n（无）";
        }

        private void RefreshActiveItems()
        {
            foreach (WorldButtonView button in _activeButtons)
            {
                if (button != null)
                {
                    Destroy(button.gameObject);
                }
            }

            _activeButtons.Clear();
            if (_run == null || _activeItemsRoot == null)
            {
                return;
            }

            int index = 0;
            foreach (string itemId in _run.ItemIds)
            {
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(itemId);
                if (item == null || item.Kind != cfg.ItemKind.Active)
                {
                    continue;
                }

                // 主动道具：右下角区，自下而上排列。
                string captured = itemId;
                WorldButtonView button = InstantiateWorldButton();
                button.gameObject.name = $"ActiveItem_{itemId}";
                button.transform.position = new Vector3(_halfW - 1.7f, -_halfH + 1.3f + index * 0.7f, 0f);
                button.Configure(
                    new Vector2(1.45f, 0.54f),
                    item.Name,
                    new Color(0.28f, 0.45f, 0.72f, 1f),
                    () => _activeItemClicked?.Invoke(captured));
                button.SetInteractable(_session != null && !_session.IsSettled);
                _activeButtons.Add(button);
                index++;
            }
        }

        private WorldButtonView InstantiateWorldButton()
        {
            if (_worldButtonPrefab != null)
            {
                return Instantiate(_worldButtonPrefab, _activeItemsRoot);
            }

            var go = new GameObject("WorldButton");
            go.transform.SetParent(_activeItemsRoot, false);
            return go.AddComponent<WorldButtonView>();
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

            foreach (WorldButtonView button in _activeButtons)
            {
                button?.SetInteractable(interactable);
            }
        }
    }
}
