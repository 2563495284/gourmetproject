using System;
using System.Collections;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 上菜动画：商人手托着放大的菜品从屏幕上方降到目标格上方，随后手先抽离，
    /// 菜品再从大变小落到格子并落定。手与菜全程带假阴影。
    /// 纯表现协程，由 <see cref="BattleWorldController"/> 驱动（StartCoroutine），不持有会话/结算状态。
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

        /// <summary>播放上菜动画，完成（含中途菜品被销毁）后回调 <paramref name="onFinished"/>。</summary>
        public IEnumerator Animate(DishPieceView piece, Vector3 target, Camera camera, float cellSize, float halfH, Action onFinished)
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

            // —— 阶段1：手托着放大的菜垂直下降到目标锚点已对齐的位置 ——
            float descend = Mathf.Max(0.0001f, _config.DescendDuration);
            float t = 0f;
            while (t < descend && piece != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / descend);
                float eased = 1f - Mathf.Pow(1f - k, 3f); // 缓出
                Vector3 palm = Vector3.Lerp(topPalm, arrivalPalm, eased);
                if (hand != null)
                {
                    hand.SetHeight(1f - eased * 0.8f); // 高空 1 → 贴近 0.2
                }

                PlacePieceAtPalm(piece, hand, palm, carryScale);
                yield return null;
            }

            if (piece == null)
            {
                if (hand != null)
                {
                    UnityEngine.Object.Destroy(hand.gameObject);
                }

                onFinished?.Invoke();
                yield break;
            }

            PlacePieceAtPalm(piece, hand, arrivalPalm, carryScale);
            piece.SetVisualScaleMultiplier(carryScale);

            // 到位高度：本体视觉中心对齐到位掌心时的离地抬升量，供脱手悬停 / 落下阶段复用。
            float arrivalLift = arrivalPalm.y - (piece.transform.position.y + piece.VisualCenterOffsetForScale(carryScale).y);

            // —— 阶段2：手先抽离屏幕，菜品悬停在到位高度（脱手不再跟手）——
            float withdraw = Mathf.Max(0.0001f, _config.WithdrawDuration);
            t = 0f;
            while (t < withdraw && piece != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / withdraw);

                if (hand != null)
                {
                    float he = k * k;
                    hand.SetPalmWorld(Vector3.Lerp(arrivalPalm, topPalm, he));
                    hand.SetHeight(0.2f + 0.8f * he);
                }

                piece.SetLiftHeight(arrivalLift);
                piece.SetVisualScaleMultiplier(carryScale);
                yield return null;
            }

            if (hand != null)
            {
                UnityEngine.Object.Destroy(hand.gameObject);
            }

            if (piece == null)
            {
                onFinished?.Invoke();
                yield break;
            }

            // —— 阶段3：菜品从大变小落到格子 ——
            float drop = Mathf.Max(0.0001f, _config.DropDuration);
            t = 0f;
            while (t < drop && piece != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / drop);

                // 根节点早已钉在目标格；落下阶段只把本体从到位高度收回贴桌、并缩回原尺寸，阴影随高度收紧变实。
                float shrink = k * k * (3f - 2f * k);
                float visualScale = Mathf.Lerp(carryScale, 1f, shrink);
                piece.SetVisualScaleMultiplier(visualScale);
                piece.SetLiftHeight(Mathf.Lerp(arrivalLift, 0f, shrink));

                yield return null;
            }

            if (piece != null)
            {
                piece.transform.position = target;
                piece.SetLiftHeight(0f);
                piece.SetVisualScaleMultiplier(1f);
                yield return piece.PlayServeLandImpactFeedback();
                // 落定后切回 Pieces 层，回到与其它棋盘食品一致的渲染顺序。
                piece.SetFlying(false);
            }

            onFinished?.Invoke();
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
