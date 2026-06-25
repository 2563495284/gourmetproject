using System;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Common
{
    public enum CartoonTransitionType
    {
        FoodWipe,
        IrisWipe,
        Fade,
        Curtain,
        PageTurn,
        SauceSplat,
        CartoonBurst,
        FoodCurtain,
        PlateWipe = FoodWipe,
    }

    public sealed class CartoonSceneTransitionData
    {
        public CartoonTransitionType TransitionType = CartoonTransitionType.PlateWipe;
        public string Message = "开饭啦！";
        public string SceneAssetName;
        public float CoverDuration = 0.32f;
        public float HoldDuration = 0.18f;
        public float RevealDuration = 0.28f;
        public Action OnCovered;
        public Action OnFinished;
    }
}
