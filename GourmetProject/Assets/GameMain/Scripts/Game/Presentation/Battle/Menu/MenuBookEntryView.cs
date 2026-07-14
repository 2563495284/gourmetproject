using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    public sealed class MenuBookEntryView : MonoBehaviour
    {
        [SerializeField] private TextMesh _text;
        [SerializeField] private BoxCollider2D _collider;

        public BoxCollider2D Collider => _collider;
        public TextMesh Text => _text;

        public void Bind(string label, Color color, float characterSize, float hitWidth, float hitHeight)
        {
            if (_text == null)
            {
                _text = GetComponentInChildren<TextMesh>(true);
            }

            if (_collider == null)
            {
                _collider = GetComponent<BoxCollider2D>();
            }

            if (_text == null || _collider == null)
            {
                Debug.LogError($"{nameof(MenuBookEntryView)} prefab 缺少 TextMesh 或 BoxCollider2D。", this);
                return;
            }

            _text.text = label;
            _text.fontSize = 64;
            _text.characterSize = characterSize;
            _text.color = color;
            _text.anchor = TextAnchor.MiddleLeft;
            _text.alignment = TextAlignment.Left;
            BattleSorting.Apply(_text.GetComponent<MeshRenderer>(), BattleSorting.WorldUi, 5);

            _collider.size = new Vector2(hitWidth, hitHeight);
            _collider.offset = new Vector2(hitWidth * 0.5f, 0f);
        }
    }
}
