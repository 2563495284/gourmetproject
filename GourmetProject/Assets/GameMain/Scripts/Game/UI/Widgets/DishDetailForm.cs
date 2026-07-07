using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Widgets
{
    /// <summary>
    /// 菜品详情界面（对应原型图 image2）：左侧菜品形状格子，顶部美味度，
    /// 中部标签（名称+描述），右侧专有名词解释框。
    /// 固定壳（遮罩/面板/名称/美味度/关闭/各容器/名词框）在 DishDetailForm.prefab，
    /// 菜品图、棋盘网格与标签行按 userData 数据驱动实例化。
    /// </summary>
    public sealed class DishDetailForm : UGuiForm
    {
        private static readonly Color GridCell = Color.white;

        [SerializeField] private Button _dimButton;
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _deliciousText;
        [SerializeField] private RectTransform _shapeContainer;
        [SerializeField] private RectTransform _tagsContainer;
        [SerializeField] private GameObject _termBox;
        [SerializeField] private Text _termText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private DishShapeCell _cellPrefab;
        [SerializeField] private DishTagLine _tagLinePrefab;

        private readonly List<GameObject> _spawned = new();
        private readonly DishSpriteProvider _spriteProvider = new();
        private Sprite _boardCellSprite;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _dimButton.onClick.AddListener(Close);
            _closeButton.onClick.AddListener(Close);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (userData is not DishDetailData data || data.Def == null)
            {
                Close();
                return;
            }

            Build(data);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            ClearSpawned();
            base.OnClose(isShutdown, userData);
        }

        private void Build(DishDetailData data)
        {
            ClearSpawned();

            DishDef def = data.Def;
            _nameText.text = def.Name;
            _deliciousText.text = $"美味度  {def.Deliciousness}";

            BuildShape(def);
            BuildTags(data);
        }

        private void BuildShape(DishDef def)
        {
            int w = def.Shape.Width;
            int h = def.Shape.Height;
            int dim = Mathf.Max(w, h);

            // 居中放在 dim×dim 网格里。
            int offX = (dim - w) / 2;
            int offY = (dim - h) / 2;

            BuildBoardGrid(dim);
            BuildFoodImage(def, dim, offX, offY);
        }

        private void BuildFoodImage(DishDef def, int dim, int offX, int offY)
        {
            var go = new GameObject("DishImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_shapeContainer, false);

            Image image = go.GetComponent<Image>();
            image.sprite = _spriteProvider.Get(def);
            image.color = Color.white;
            image.raycastTarget = false;
            image.preserveAspect = false;

            int w = def.Shape.Width;
            int h = def.Shape.Height;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2((float)offX / dim, 1f - (float)(offY + h) / dim);
            rect.anchorMax = new Vector2((float)(offX + w) / dim, 1f - (float)offY / dim);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            _spawned.Add(go);
        }

        private void BuildBoardGrid(int dim)
        {
            for (int y = 0; y < dim; y++)
            {
                for (int x = 0; x < dim; x++)
                {
                    float minX = (float)x / dim;
                    float maxX = (float)(x + 1) / dim;
                    float minY = 1f - (float)(y + 1) / dim;
                    float maxY = 1f - (float)y / dim;

                    DishShapeCell cell = Instantiate(_cellPrefab, _shapeContainer);
                    var rect = (RectTransform)cell.transform;
                    rect.anchorMin = new Vector2(minX, minY);
                    rect.anchorMax = new Vector2(maxX, maxY);
                    rect.offsetMin = new Vector2(3f, 3f);
                    rect.offsetMax = new Vector2(-3f, -3f);
                    rect.localScale = Vector3.one;
                    cell.SetSprite(BoardCellSprite);
                    cell.SetColor(GridCell);
                    _spawned.Add(cell.gameObject);
                }
            }
        }

        private Sprite BoardCellSprite
        {
            get
            {
                if (_boardCellSprite == null)
                {
                    _boardCellSprite = Resources.Load<Sprite>("Sprites/UI/board_cell");
                }

                return _boardCellSprite;
            }
        }

        private void BuildTags(DishDetailData data)
        {
            GameplayDatabase db = data.Database;
            float top = 1f;
            float rowH = 0.16f;
            int shown = 0;

            List<string> lines = DishInfoText.TagLines(
                data.SkillIds, data.FlavorId, db, out List<string> terms, data.SkillSources);

            foreach (string line in lines)
            {
                float rowTop = top - shown * rowH;
                float rowBottom = rowTop - rowH + 0.01f;
                if (rowBottom < 0f)
                {
                    break;
                }

                DishTagLine tagLine = Instantiate(_tagLinePrefab, _tagsContainer);
                var rect = (RectTransform)tagLine.transform;
                rect.anchorMin = new Vector2(0f, rowBottom);
                rect.anchorMax = new Vector2(1f, rowTop);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
                tagLine.SetText(line);
                _spawned.Add(tagLine.gameObject);
                shown++;
            }

            string termText = DishInfoText.TermBlock(terms);
            bool hasTerm = !string.IsNullOrEmpty(termText);
            _termBox.SetActive(hasTerm);
            if (hasTerm)
            {
                _termText.text = termText;
            }
        }

        private void ClearSpawned()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }
    }
}
