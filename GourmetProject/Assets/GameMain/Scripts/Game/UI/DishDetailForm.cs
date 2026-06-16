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
    /// 中部标签（名称+描述），右侧专有名词解释框。数据经 userData 传入，全部代码构建。
    /// </summary>
    public sealed class DishDetailForm : UGuiForm
    {
        private static readonly Color Dim = new(0f, 0f, 0f, 0.7f);
        private static readonly Color Box = new(0.16f, 0.16f, 0.2f, 1f);
        private static readonly Color CellOn = new(1f, 0.72f, 0.3f, 1f);
        private static readonly Color CellOff = new(1f, 1f, 1f, 0.06f);

        private RectTransform _content;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            UiBuilder.AddImage(CachedTransform, "Dim", Dim, 0f, 0f, 1f, 1f)
                .gameObject.AddComponent<Button>().onClick.AddListener(Close);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (_content != null)
            {
                Destroy(_content.gameObject);
                _content = null;
            }

            if (userData is not DishDetailData data || data.Def == null)
            {
                Close();
                return;
            }

            Build(data);
        }

        private void Build(DishDetailData data)
        {
            DishDef def = data.Def;

            _content = UiBuilder.NewRect("Content", CachedTransform);
            UiBuilder.Anchor(_content, 0.22f, 0.16f, 0.78f, 0.84f);
            UiBuilder.AddImage(_content, "Box", Box, 0f, 0f, 1f, 1f);

            UiBuilder.AddText(_content, "Name", def.Name, 36, Color.white, 0.05f, 0.86f, 0.95f, 0.98f);
            UiBuilder.AddText(_content, "Delicious", $"美味度  {def.Deliciousness}", 26, new Color(1f, 0.85f, 0.4f, 1f), 0.05f, 0.78f, 0.5f, 0.86f, TextAnchor.MiddleLeft);

            BuildShape(def);
            BuildTags(data);

            UiBuilder.AddButton(_content, "Close", "关闭", new Color(0.3f, 0.3f, 0.35f, 1f), 0.4f, 0.03f, 0.6f, 0.11f, Close, 24);
        }

        private void BuildShape(DishDef def)
        {
            RectTransform shapeRoot = UiBuilder.NewRect("Shape", _content);
            UiBuilder.Anchor(shapeRoot, 0.06f, 0.30f, 0.40f, 0.72f);
            UiBuilder.AddImage(shapeRoot, "ShapeBg", new Color(1f, 1f, 1f, 0.04f), 0f, 0f, 1f, 1f);

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
                    UiBuilder.AddImage(shapeRoot, $"S_{x}_{y}", on ? CellOn : CellOff, minX, minY, maxX, maxY, 3f);
                }
            }
        }

        private void BuildTags(DishDetailData data)
        {
            RectTransform tagRoot = UiBuilder.NewRect("Tags", _content);
            UiBuilder.Anchor(tagRoot, 0.44f, 0.14f, 0.96f, 0.72f);

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

                UiBuilder.AddText(tagRoot, $"Tag_{shown}", line, 20, Color.white,
                    0f, rowBottom, 1f, rowTop, TextAnchor.UpperLeft);
                shown++;
            }

            BuildTermBox(terms);
        }

        private void BuildTermBox(List<string> termIds)
        {
            string termText = DishInfoText.TermBlock(termIds);
            if (string.IsNullOrEmpty(termText))
            {
                return;
            }

            RectTransform termRoot = UiBuilder.NewRect("Terms", _content);
            UiBuilder.Anchor(termRoot, 0.44f, 0.14f, 0.96f, 0.30f);
            UiBuilder.AddImage(termRoot, "TermBg", new Color(1f, 1f, 1f, 0.06f), 0f, 0f, 1f, 1f, 2f);

            UiBuilder.AddText(termRoot, "TermText", termText, 18, new Color(0.9f, 0.9f, 0.7f, 1f), 0.03f, 0.05f, 0.97f, 0.95f, TextAnchor.UpperLeft);
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }
    }
}
