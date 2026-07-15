using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>风味标签条目：视觉结构由 prefab 提供，脚本只绑定文字。</summary>
    public sealed class FoodFlavorTagView : MonoBehaviour
    {
        [SerializeField] private Text _labelText;

        public void Bind(string label)
        {
            if (!ValidateReferences())
            {
                return;
            }

            _labelText.text = label ?? string.Empty;
        }

        private void Awake()
        {
            ValidateReferences();
        }

        private void Reset()
        {
            ValidateReferences();
        }

        private bool ValidateReferences()
        {
            if (_labelText != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(FoodFlavorTagView)} on '{name}' is missing prefab reference '{nameof(_labelText)}'.", this);
            return false;
        }
    }
}
