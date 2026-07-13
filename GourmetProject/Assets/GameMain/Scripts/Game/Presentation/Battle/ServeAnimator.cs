using System.Threading;
using DG.Tweening;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 上菜动画：商人手托着放大的菜品从屏幕上方降到目标格上方，随后手先抽离，
    /// 菜品再从大变小落到格子并落定。手与菜全程带假阴影。
    /// 纯表现异步动画，由 <see cref="BattleWorldController"/> 驱动（Awaitable），不持有会话/结算状态。
    /// </summary>
    public sealed class ServeAnimator
    {
        public struct Config
        {
            public float CarryScale;
            public float DescendDuration;
            public float WithdrawDuration;
            public float DropDuration;
        }

        private readonly Transform _parent;
        private readonly ServeHandView _handPrefab;
        private readonly Config _config;

        public ServeAnimator(Transform parent, ServeHandView handPrefab, Config config)
        {
            _parent = parent;
            _handPrefab = handPrefab;
            _config = config;
        }

        /// <summary>播放上菜动画，完成（含中途菜品被销毁）后返回。</summary>
        public async Awaitable AnimateAsync(DishPieceView piece, Vector3 target, Camera camera, float cellSize, float halfH, CancellationToken cancellationToken)
        {
            // 手世界高度：约 4 格高，受半屏高约束，保证起点能完全藏到屏幕上方外。
            float handHeight = Mathf.Clamp(cellSize * 4.2f, 2.4f, halfH * 1.5f);

            ServeHandView hand = null;
            if (_handPrefab != null)
            {
                hand = UnityEngine.Object.Instantiate(_handPrefab, _parent);
                hand.gameObject.name = "ServeHand";
                hand.Build(camera, handHeight);
            }

            // 到位点：掌心锚点与食品视觉中心对齐，同时保证菜品根节点锚点已经落在目标格锚点上。
            // 这样脱手时不会从目标正上方开始落，而是在对齐位置上只做缩放/高度反馈。
            float carryScale = Mathf.Max(0.0001f, _config.CarryScale);
            Vector3 carryCenterOffset = piece.VisualCenterOffsetForScale(carryScale);
            Vector3 arrivalPalm = target + carryCenterOffset;
            Vector3 topPalm = new Vector3(arrivalPalm.x, halfH + handHeight, 0f);

            // 根节点全程钉在目标格（阴影留在地面），只用本体局部抬升表现飞行高度；
            // 本体切到 PiecesFlying 压在已摆放食品之上，阴影留在 Pieces 地面层不盖菜。
            piece.transform.position = target;
            piece.SetFlying(true);
            piece.SetVisualScaleMultiplier(carryScale);
            PlacePieceAtPalm(piece, hand, topPalm, carryScale);

            try
            {
                // —— 阶段1：手托着放大的菜垂直下降到目标锚点已对齐的位置 ——
                float descend = Mathf.Max(0.0001f, _config.DescendDuration);
                Tween descendTween = DOVirtual.Float(0f, 1f, descend, k =>
                    {
                        if (piece == null)
                        {
                            return;
                        }

                        float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f); // 缓出
                        Vector3 palm = Vector3.Lerp(topPalm, arrivalPalm, eased);
                        if (hand != null)
                        {
                            hand.SetHeight(1f - eased * 0.8f); // 高空 1 → 贴近 0.2
                        }

                        PlacePieceAtPalm(piece, hand, palm, carryScale);
                    })
                    .SetEase(Ease.Linear)
                    .SetLink(piece.gameObject);
                await PresentationTween.AwaitCompletionAsync(descendTween, cancellationToken);

                if (piece == null)
                {
                    return;
                }

                PlacePieceAtPalm(piece, hand, arrivalPalm, carryScale);
                piece.SetVisualScaleMultiplier(carryScale);

                // 到位高度：本体视觉中心对齐到位掌心时的离地抬升量，供脱手悬停 / 落下阶段复用。
                float arrivalLift = arrivalPalm.y - (piece.transform.position.y + piece.VisualCenterOffsetForScale(carryScale).y);

                // —— 阶段2：手先抽离屏幕，菜品悬停在到位高度（脱手不再跟手）——
                float withdraw = Mathf.Max(0.0001f, _config.WithdrawDuration);
                Tween withdrawTween = DOVirtual.Float(0f, 1f, withdraw, k =>
                    {
                        if (piece == null)
                        {
                            return;
                        }

                        float he = Mathf.Clamp01(k) * Mathf.Clamp01(k);
                        if (hand != null)
                        {
                            hand.SetPalmWorld(Vector3.Lerp(arrivalPalm, topPalm, he));
                            hand.SetHeight(0.2f + 0.8f * he);
                        }

                        piece.SetLiftHeight(arrivalLift);
                        piece.SetVisualScaleMultiplier(carryScale);
                    })
                    .SetEase(Ease.Linear)
                    .SetLink(piece.gameObject);
                await PresentationTween.AwaitCompletionAsync(withdrawTween, cancellationToken);

                if (hand != null)
                {
                    UnityEngine.Object.Destroy(hand.gameObject);
                    hand = null;
                }

                if (piece == null)
                {
                    return;
                }

                // —— 阶段3：菜品从大变小落到格子 ——
                float drop = Mathf.Max(0.0001f, _config.DropDuration);
                Tween dropTween = DOVirtual.Float(0f, 1f, drop, k =>
                    {
                        if (piece == null)
                        {
                            return;
                        }

                        // 根节点早已钉在目标格；落下阶段只把本体从到位高度收回贴桌、并缩回原尺寸，阴影随高度收紧变实。
                        float clamped = Mathf.Clamp01(k);
                        float shrink = clamped * clamped * (3f - 2f * clamped);
                        float visualScale = Mathf.Lerp(carryScale, 1f, shrink);
                        piece.SetVisualScaleMultiplier(visualScale);
                        piece.SetLiftHeight(Mathf.Lerp(arrivalLift, 0f, shrink));
                    })
                    .SetEase(Ease.Linear)
                    .SetLink(piece.gameObject);
                await PresentationTween.AwaitCompletionAsync(dropTween, cancellationToken);

                if (piece != null)
                {
                    piece.transform.position = target;
                    piece.SetLiftHeight(0f);
                    piece.SetVisualScaleMultiplier(1f);
                    await piece.PlayServeLandImpactFeedbackAsync(cancellationToken);
                    // 落定后切回 Pieces 层，回到与其它餐桌食品一致的渲染顺序。
                    piece.SetFlying(false);
                }
            }
            finally
            {
                if (hand != null)
                {
                    UnityEngine.Object.Destroy(hand.gameObject);
                }
            }
        }

        /// <summary>把掌心锚点与当前缩放下的食品视觉中心对齐。</summary>
        /// <remarks>根节点全程钉在目标格、水平不动；这里只把本体沿世界 Y 抬升到掌心高度，阴影留在地面脚印中心。</remarks>
        private static void PlacePieceAtPalm(DishPieceView piece, ServeHandView hand, Vector3 palm, float visualScale)
        {
            if (piece == null)
            {
                return;
            }

            Vector3 anchor = palm;
            if (hand != null)
            {
                hand.SetPalmWorld(palm);
                anchor = hand.PalmWorldPosition;
            }

            float groundCenterY = piece.transform.position.y + piece.VisualCenterOffsetForScale(visualScale).y;
            piece.SetLiftHeight(anchor.y - groundCenterY);
        }
    }
}
