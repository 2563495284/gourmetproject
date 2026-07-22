using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>战斗菜谱卡内的一条剩余食物：名称与不可放置标记均由 prefab 固定结构提供。</summary>
    public sealed class RecipeDishEntryView : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _unavailableMark;

        public void Bind(string dishName, bool canPlace)
        {
            if (_nameText != null)
            {
                _nameText.text = dishName ?? string.Empty;
            }

            if (_unavailableMark != null)
            {
                _unavailableMark.gameObject.SetActive(!canPlace);
            }
        }
    }
}
