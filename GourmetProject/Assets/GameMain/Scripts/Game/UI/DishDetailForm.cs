using System.Collections.Generic;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 菜品详情界面（对应原型图 image2）：左侧菜品形状格子，顶部美味度，
    /// 中部标签（名称+描述），右侧专有名词解释框。
    /// 固定壳（遮罩/面板/名称/美味度/关闭/各容器/名词框）在 DishDetailForm.prefab，
    /// 形状格子与标签行用 DishShapeCell / DishTagLine 子 prefab 按 userData 数据驱动实例化。
    /// </summary>
    public sealed class DishDetailForm : UGuiForm
    {
        private static readonly Color CellOn = new(1f, 0.72f, 0.3f, 1f);
        private static readonly Color CellOff = new(1f, 1f, 1f, 0.06f);

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
            var filled = new HashSet<GridPos>(def.Shape.Cells);

            // 居中放在 dim×dim 网格里。
            int offX = (dim - w) / 2;
            int offY = (dim - h) / 2;

            for (int y = 0; y < dim; y++)
            {
                for (int x = 0; x < dim; x++)
                {
                    float minX = (float)x / dim;
                    float maxX = (float)(x + 1) / dim;
                    float minY = 1f - (float)(y + 1) / dim;
                    float maxY = 1f - (float)y / dim;
                    bool on = filled.Contains(new GridPos(x - offX, y - offY));

                    DishShapeCell cell = Instantiate(_cellPrefab, _shapeContainer);
                    var rect = (RectTransform)cell.transform;
                    rect.anchorMin = new Vector2(minX, minY);
                    rect.anchorMax = new Vector2(maxX, maxY);
                    rect.offsetMin = new Vector2(3f, 3f);
                    rect.offsetMax = new Vector2(-3f, -3f);
                    rect.localScale = Vector3.one;
                    cell.SetColor(on ? CellOn : CellOff);
                    _spawned.Add(cell.gameObject);
                }
            }
        }

        private void BuildTags(DishDetailData data)
        {
            GameplayDatabase db = data.Database;
            float top = 1f;
            float rowH = 0.16f;
            int shown = 0;

            List<string> lines = DishInfoText.TagLines(data.TagIds, db, out List<string> terms);

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
