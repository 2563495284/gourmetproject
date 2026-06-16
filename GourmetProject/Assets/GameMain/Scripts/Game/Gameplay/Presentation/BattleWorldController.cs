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
    /// <summary>战斗内场景表现根控制器：棋盘、后厨、手牌区与拖拽放置。</summary>
    public sealed class BattleWorldController : MonoBehaviour
    {
        public const float Gap = 0f;
        private const float MaxCellSize = 1.2f;
        private const float MinCellSize = 0.42f;

        // 回退视口半宽/半高（16:9 参考：orthographicSize 5.4）。
        private const float FallbackHalfW = 9.6f;
        private const float FallbackHalfH = 5.4f;

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
        private BoardView _boardView;
        private Transform _piecesRoot;
        private Transform _menuRoot;
        private Transform _fxRoot;
        private Camera _camera;
        private TextMesh _scoreText;
        private TextMesh _messageText;
        private TextMesh _itemsText;
        private WorldButtonView[] _recipeButtons;
        private WorldButtonView _eatButton;
        private SettlementSequencer _sequencer;
        private bool _settling;

        private Action<string> _messageSink;
        private Action _eatClicked;
        private Action _overviewClicked;
        private Action<string> _activeItemClicked;
        private Action<DishInstance> _dishClicked;
        private Action _stateChanged;

        public static BattleWorldController GetOrCreate()
        {
            GameObject root = GameObject.Find("BattleWorldRoot");
            if (root == null)
            {
                root = new GameObject("BattleWorldRoot");
            }

            BattleWorldController controller = root.GetComponent<BattleWorldController>();
            if (controller == null)
            {
                controller = root.AddComponent<BattleWorldController>();
            }

            return controller;
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
            _camera = Camera.main;

            gameObject.SetActive(true);
            _settling = false;
            ComputeViewport();
            ClearChildren();
            BuildRoots();
            BuildBackground();
            BuildBoard(session.Board);
            BuildSequencer();
            BuildMenus();
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

        private void BuildRoots()
        {
            _menuRoot = NewChild("SceneBattleMenu");
            _piecesRoot = NewChild("SceneDishPieces");
            _fxRoot = NewChild("SceneSettlementFx");
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

        private void BuildSequencer()
        {
            _sequencer = gameObject.GetComponent<SettlementSequencer>();
            if (_sequencer == null)
            {
                _sequencer = gameObject.AddComponent<SettlementSequencer>();
            }
        }

        private void BuildBackground()
        {
            Transform backgroundRoot = NewChild("SceneBackground");
            var go = new GameObject("TableclothBackground");
            go.transform.SetParent(backgroundRoot, false);
            go.transform.position = new Vector3(0f, 0f, 0.2f);

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Resources.Load<Sprite>("Sprites/Backgrounds/battle_table_backpack") ?? CreatePixelSprite();
            renderer.color = Color.white;
            BattleSorting.Apply(renderer, BattleSorting.Background);
            SpriteRenderStyle.ApplyUnlitMaterial(renderer);

            float height = _camera != null && _camera.orthographic ? _camera.orthographicSize * 2f : 10.8f;
            float width = height * (_camera != null ? _camera.aspect : 16f / 9f);
            FitSpriteToCover(go.transform, renderer, width, height);
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

            Transform boardRoot = NewChild("SceneBoard");
            _boardView = boardRoot.gameObject.AddComponent<BoardView>();
            _boardView.Build(board, _cellSize, Gap, _boardCenter, OnCellClicked);
        }

        private void BuildMenus()
        {
            // 控件按 image1.png 线框分区，锚定到视口半宽/半高边缘。
            // 分数：顶部居中。被动道具：右侧竖栏。
            _scoreText = CreateText(_menuRoot, "ScoreText", new Vector3(0f, _halfH - 0.55f, 0f), 56, TextAnchor.MiddleCenter);
            _messageText = CreateText(_menuRoot, "MessageText", new Vector3(0f, -_halfH + 1.6f, 0f), 34, TextAnchor.MiddleCenter);
            _itemsText = CreateText(_menuRoot, "ItemsText", new Vector3(_halfW - 2.1f, _halfH - 1.6f, 0f), 28, TextAnchor.UpperCenter);

            // 总览：左上角。
            WorldButtonView.Create(
                _menuRoot,
                "OverviewButton",
                new Vector3(-_halfW + 1.1f, _halfH - 0.5f, 0f),
                new Vector2(1.15f, 0.52f),
                "总览",
                new Color(0.38f, 0.31f, 0.26f, 1f),
                () => _overviewClicked?.Invoke());

            // 后厨：左侧竖排菜谱 +「上菜/剩N」。
            _recipeButtons = new WorldButtonView[GameRun.RecipeSlotCount];
            for (int i = 0; i < _recipeButtons.Length; i++)
            {
                int index = i;
                _recipeButtons[i] = WorldButtonView.Create(
                    _menuRoot,
                    $"RecipeButton_{i}",
                    new Vector3(-_halfW + 1.7f, 0.8f - i * 1.1f, 0f),
                    new Vector2(1.7f, 0.78f),
                    $"菜谱{i + 1}",
                    new Color(0.82f, 0.48f, 0.18f, 1f),
                    () => TryServeDish(index));
            }

            // 吃!：棋盘正下方居中。
            _eatButton = WorldButtonView.Create(
                _menuRoot,
                "EatButton",
                new Vector3(_boardCenter.x, -_halfH + 0.85f, 0f),
                new Vector2(1.55f, 0.72f),
                "吃!",
                new Color(0.95f, 0.35f, 0.12f, 1f),
                () => _eatClicked?.Invoke());
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
            GameObject go = new GameObject($"Dish_{dish.Id}_{dish.Def.Id}");
            go.transform.SetParent(_piecesRoot, false);
            go.transform.position = _boardView.Mapper.CellCenter(dish.Placement.Origin);

            DishPieceView piece = go.AddComponent<DishPieceView>();
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
                WorldButtonView button = WorldButtonView.Create(
                    _menuRoot,
                    $"ActiveItem_{itemId}",
                    new Vector3(_halfW - 1.7f, -_halfH + 1.3f + index * 0.7f, 0f),
                    new Vector2(1.45f, 0.54f),
                    item.Name,
                    new Color(0.28f, 0.45f, 0.72f, 1f),
                    () => _activeItemClicked?.Invoke(captured));
                button.SetInteractable(_session != null && !_session.IsSettled);
                _activeButtons.Add(button);
                index++;
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

        private Transform NewChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private static Sprite CreatePixelSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
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

        private TextMesh CreateText(Transform parent, string name, Vector3 position, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            TextMesh text = go.AddComponent<TextMesh>();
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.fontSize = fontSize;
            text.characterSize = 0.08f;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            BattleSorting.Apply(renderer, BattleSorting.WorldUi);
            return text;
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

        private void ClearChildren()
        {
            StopAllCoroutines();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            _activeButtons.Clear();
            _placedPieces.Clear();
        }
    }
}
