using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 世界空间目标箭头：起点固定在被移动的菜品原位，终点跟随鼠标 / 光标菜品。
    /// 固定结构（LineRenderer + 箭头 SpriteRenderer）预拼在 prefab 上，运行时只按格子尺寸调整线宽和箭头大小。
    /// </summary>
    public sealed class WorldTargetArrow : MonoBehaviour
    {
        private LineRenderer _line;
        private SpriteRenderer _head;
        private float _lineWidth = 0.12f;
        private float _headGap = 0.28f;

        public static WorldTargetArrow Create(WorldTargetArrow prefab, Transform parent, float cellSize)
        {
            if (prefab == null)
            {
                Debug.LogError($"{nameof(WorldTargetArrow)} 缺少 prefab。");
                return null;
            }

            WorldTargetArrow arrow = Instantiate(prefab, parent);
            arrow.gameObject.name = "WorldTargetArrow";
            arrow.Build(cellSize);
            return arrow;
        }

        private void Build(float cellSize)
        {
            _lineWidth = Mathf.Max(0.06f, cellSize * 0.16f);
            _headGap = Mathf.Max(0.12f, cellSize * 0.42f);

            if (_line == null)
            {
                _line = GetComponentInChildren<LineRenderer>(true);
            }

            if (_head == null)
            {
                _head = GetComponentInChildren<SpriteRenderer>(true);
            }

            if (_line == null || _head == null)
            {
                Debug.LogError($"{nameof(WorldTargetArrow)} prefab 缺少 LineRenderer 或箭头 SpriteRenderer。", this);
                return;
            }

            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.numCapVertices = 4;
            _line.textureMode = LineTextureMode.Stretch;
            _line.widthMultiplier = _lineWidth;
            if (_line.sharedMaterial == null)
            {
                _line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            }

            var lineColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            _line.startColor = lineColor;
            _line.endColor = lineColor;
            BattleSorting.Apply(_line, BattleSorting.WorldUi, BattleSorting.OrderTargetArrowLine);

            if (_head.sprite == null)
            {
                _head.sprite = Resources.Load<Sprite>("Sprites/UI/ArrowHead");
            }

            _head.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            SpriteRenderStyle.ApplyUnlitMaterial(_head);
            BattleSorting.Apply(_head, BattleSorting.WorldUi, BattleSorting.OrderTargetArrowHead);

            float headSize = Mathf.Max(0.2f, cellSize * 0.55f);
            Sprite headSprite = _head.sprite;
            if (headSprite != null)
            {
                Vector2 bounds = headSprite.bounds.size;
                float sx = bounds.x > 0f ? headSize / bounds.x : headSize;
                float sy = bounds.y > 0f ? headSize / bounds.y : headSize;
                _head.transform.localScale = new Vector3(sx, sy, 1f);
            }
            else
            {
                _head.transform.localScale = Vector3.one * headSize;
            }
        }

        public void SetEndpoints(Vector3 startWorld, Vector3 endWorld)
        {
            if (_line == null)
            {
                return;
            }

            startWorld.z = 0f;
            endWorld.z = 0f;

            Vector3 delta = endWorld - startWorld;
            float distance = delta.magnitude;
            Vector3 dir = distance > 0.001f ? delta / distance : Vector3.right;
            Vector3 lineEnd = endWorld - dir * _headGap;

            _line.SetPosition(0, new Vector3(startWorld.x, startWorld.y, -0.02f));
            _line.SetPosition(1, new Vector3(lineEnd.x, lineEnd.y, -0.02f));

            if (_head != null)
            {
                _head.transform.position = new Vector3(endWorld.x, endWorld.y, -0.03f);
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                _head.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        public void Destroy()
        {
            if (this != null)
            {
                Object.Destroy(gameObject);
            }
        }
    }
}
