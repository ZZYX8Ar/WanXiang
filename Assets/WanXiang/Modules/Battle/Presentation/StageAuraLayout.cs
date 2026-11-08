// ============================================================================
//  万相 · 光环布局（表现层重构 R2）
//  ---------------------------------------------------------------------------
//  从 BattleStage2D.cs 抽出的**纯函数**：脚下光环的 localPosition / localScale 计算。
//  原来 AuraFoot 与 StatusAura 两处算法几乎相同却各写一遍（宽度倍率 1.55/1.5、
//  高度倍率 0.95/0.75），改一处忘一处就会错位。
//
//  · ComputeRaw 只吃数值 ⇒ 可单测、可复算，不依赖 Unity 场景
//  · Compute 从 Sprite / lossyScale 取数后转调 ComputeRaw
//  · Policy 可替换：换算法只 RegisterPolicy 一行；传 null 即回退默认
//  ⛔ 无状态、不缓存、不持有 GameObject ⇒ 可单独删除
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public struct AuraLayoutResult
    {
        public Vector3 LocalPosition;
        public Vector3 LocalScale;
    }

    public static class StageAuraLayout
    {
        public const float DefaultYFactor = 0.52f;   // 贴到脚底：sprite 高度的一半再往下一点
        public const float DefaultZ = 0.02f;

        public delegate AuraLayoutResult Policy(Sprite body, Vector3 bodyLossy, Sprite ring,
                                               float widthMul, float heightMul);

        private static Policy _policy;

        /// <summary>注册自定义布局算法；传 null = 回退默认（效果可整体注销）。</summary>
        public static void RegisterPolicy(Policy p) => _policy = p;

        public static bool HasCustomPolicy => _policy != null;

        public static AuraLayoutResult Compute(Sprite body, Vector3 bodyLossy, Sprite ring,
                                              float widthMul, float heightMul)
        {
            if (_policy != null) return _policy(body, bodyLossy, ring, widthMul, heightMul);
            var bs = body != null ? body.bounds.size : new Vector3(1f, 1f, 0f);
            var rs = ring != null ? ring.bounds.size : new Vector3(1f, 1f, 0f);
            return ComputeRaw(bs, bodyLossy, rs, widthMul, heightMul, body != null);
        }

        /// <summary>纯数值版：与重构前 BattleStage2D 的公式逐项一致。</summary>
        public static AuraLayoutResult ComputeRaw(Vector3 bodySize, Vector3 bodyLossy, Vector3 ringSize,
                                                  float widthMul, float heightMul, bool hasBody = true)
        {
            var r = new AuraLayoutResult();

            // 位置：Body 本地空间（单位 = 贴图 bounds），用 sprite 高度的一半往下挪
            r.LocalPosition = new Vector3(0f,
                -(hasBody ? bodySize.y * DefaultYFactor : 0.5f), DefaultZ);

            // 尺寸：世界宽 = 贴图宽 × lossyScale.x；再除以父级 lossyScale 才不会缩没
            float worldW = Mathf.Abs(bodySize.x * bodyLossy.x);
            r.LocalScale = new Vector3(
                worldW * widthMul / Mathf.Max(0.0001f, ringSize.x * Mathf.Abs(bodyLossy.x)),
                worldW * heightMul / Mathf.Max(0.0001f, ringSize.y * Mathf.Abs(bodyLossy.y)),
                1f);
            return r;
        }
    }
}
