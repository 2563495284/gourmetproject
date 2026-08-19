using System;
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
    [PassiveItemModel("item_cake_req_minus_30")]
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
        public override void OnRemoved()
        {
            Run?.ClearRetainedHappyCakeLayers();
        }

        public override bool TryGetCakeRetainFraction(out float value)
        {
            value = Value;
            return true;
        }
    }

    /// <summary>名刀·加护：不死，生效后恢复到配置颗数的红心。</summary>
    [Preserve]
    [PassiveItemModel("item_famous_knife")]
    public sealed class UndyingModel : PassiveItemModel
    {
        public override bool IsUndying() => true;

        public override int UndyingRestoreHearts()
            => Math.Max(1, (int)Math.Round(Value, MidpointRounding.AwayFromZero));
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
    [PassiveItemModel("item_extra_active_slots_max")]
    public sealed class ExtraActiveSlotModel : PassiveItemModel
    {
        public override int ExtraActiveSlots() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_trash_upgrade")]
    [PassiveItemModel("item_trash_expand")]
    [PassiveItemModel("item_trash_evolve")]
    [PassiveItemModel("item_trash_mutate")]
    public sealed class FoodDiscardLimitBonusModel : PassiveItemModel
    {
        public override int FoodDiscardLimitBonus() => System.Math.Max(0, (int)Value);
    }

    [Preserve]
    [PassiveItemModel("item_block_active")]
    public sealed class BlockActiveModel : PassiveItemModel
    {
        public override bool IsIconUsed => false;

        public override void OnAcquired()
        {
            if (Run != null)
            {
                Run.Gold += System.Math.Max(0, (int)Value);
                MarkIconUsed();
            }
        }

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

        public override void OnSweetTransferTriggered(SweetTransferOccurrence occurrence)
        {
            if (!IsStillHeld)
            {
                return;
            }

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
        public override bool TryGetSweetTransferTargetMultiplier(out float value)
        {
            value = Value;
            return value > 0f;
        }
    }

    [Preserve]
    [PassiveItemModel("item_transfer_source_mult")]
    public sealed class TransferSourceMultModel : SweetTransferCounterModel
    {
        public override bool TryGetSweetTransferSourceMultiplier(out float value)
        {
            value = Value;
            return value > 0f;
        }
    }

    [Preserve]
    [PassiveItemModel("item_transfer_target_flat")]
    public sealed class TransferTargetFlatModel : SweetTransferCounterModel
    {
        public override bool TryGetSweetTransferTargetFlat(out float value)
        {
            value = Value;
            return value > 0f;
        }
    }

    [Preserve]
    [PassiveItemModel("item_transfer_source_flat")]
    public sealed class TransferSourceFlatModel : SweetTransferCounterModel
    {
        public override bool TryGetSweetTransferSourceFlat(out float value)
        {
            value = Value;
            return value > 0f;
        }
    }
}
