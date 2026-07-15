using System.Globalization;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Scoring;
using UnityEngine.Scripting;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Meta.Passives
{
    [Preserve]
    [PassiveItemModel("item_cake_init_bonus")]
    public sealed class CakeInitBonusModel : PassiveItemModel
    {
        public override int CakeInitialLayers() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_cake_req_minus")]
    public sealed class CakeReqMinusModel : PassiveItemModel
    {
        public override int CakeThresholdReduction() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_cake_accel")]
    public sealed class CakeAccelModel : PassiveItemModel
    {
        public override int CakeAccelBonus() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_cake_retain")]
    public sealed class CakeRetainModel : PassiveItemModel
    {
        public override bool TryGetCakeRetainFraction(out float value)
        {
            value = Value;
            return true;
        }
    }

    /// <summary>名刀·加护：不死。</summary>
    [Preserve]
    [PassiveItemModel("item_famous_knife")]
    public sealed class UndyingModel : PassiveItemModel
    {
        public override bool IsUndying() => true;
    }

    [Preserve]
    [PassiveItemModel("item_adjust_plus1")]
    public sealed class AdjustCountModel : PassiveItemModel
    {
        public override int AdjustCountBonus() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_adjust_to_mult")]
    public sealed class AdjustToMultModel : PassiveItemModel
    {
        public override bool TryGetAdjustToMult(out float value)
        {
            value = Value;
            return true;
        }
    }

    [Preserve]
    [PassiveItemModel("item_free_move_first")]
    public sealed class FreeMoveFirstModel : PassiveItemModel
    {
        public override bool FreeMoveFirstServe() => true;
    }

    [Preserve]
    [PassiveItemModel("item_stargaze_every5")]
    public sealed class StarGazeEveryModel : PassiveItemModel
    {
        public override int StarGazeEvery() => PassiveParam.ParseInt(Param, "every", 0);
    }

    [Preserve]
    [PassiveItemModel("item_stargaze_first3")]
    public sealed class StarGazeFirstModel : PassiveItemModel
    {
        public override int StarGazeFirst() => PassiveParam.ParseInt(Param, "first", 0);
    }

    [Preserve]
    [PassiveItemModel("item_extra_active_slots")]
    public sealed class ExtraActiveSlotModel : PassiveItemModel
    {
        public override int ExtraActiveSlots() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_block_active")]
    public sealed class BlockActiveModel : PassiveItemModel
    {
        public override bool BlocksActiveItems() => true;
    }

    public abstract class SweetTransferCounterModel : PassiveItemModel
    {
        private int _transferCount;

        public int TransferCount => _transferCount;

        public override string InfoText => _transferCount.ToString(CultureInfo.InvariantCulture);

        public override void ApplyToBattle(BattleSession session)
        {
            if (session != null)
            {
                session.SweetTransferTriggered += OnSweetTransferTriggered;
            }
        }

        public override void OnSweetTransferTriggered(SkillTransferRequest request)
        {
            _transferCount++;
            Flash();
            RefreshInfoText();
        }

        public override string CaptureState()
            => JoinState(CaptureIconState(), $"count:{_transferCount.ToString(CultureInfo.InvariantCulture)}");

        public override void RestoreState(string data)
        {
            RestoreIconState(data);
            _transferCount = System.Math.Max(0, ParseStateInt(data, "count", 0));
        }
    }

    [Preserve]
    [PassiveItemModel("item_transfer_target_mult")]
    public sealed class TransferTargetMultModel : SweetTransferCounterModel
    {
    }

    [Preserve]
    [PassiveItemModel("item_transfer_source_mult")]
    public sealed class TransferSourceMultModel : SweetTransferCounterModel
    {
    }

    /// <summary>TODO(passive-item): 需选目标/食物转换子系统；获得时占位。</summary>
    [Preserve]
    [PassiveItemModel("item_food_convert")]
    public sealed class FoodConvertModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            Log.Info($"OnAcquire 效果 FoodConvert({ItemId}) 尚未实装，已忽略。", "Item");
        }
    }
}
