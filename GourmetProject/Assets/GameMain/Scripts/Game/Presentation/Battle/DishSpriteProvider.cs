using GourmetProject.Game;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>按食物名称约定加载食物 sprite，并提供兜底方块。</summary>
    public sealed class DishSpriteProvider
    {
        private Sprite _fallback;

        public Sprite Get(DishDef dish)
        {
            Sprite sprite = ContentIconLoader.LoadDish(dish);
            if (sprite != null)
            {
                return sprite;
            }

            return Fallback;
        }

        public Sprite Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    texture.SetPixel(0, 0, Color.white);
                    texture.Apply();
                    _fallback = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                    _fallback.name = "GeneratedWhitePixel";
                }

                return _fallback;
            }
        }
    }
}
