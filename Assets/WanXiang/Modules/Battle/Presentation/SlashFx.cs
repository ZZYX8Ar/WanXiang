// ============================================================================
//  SlashFx —— 斩击弧光播放（T6，2026-10-24）
//  ---------------------------------------------------------------------------
//  一次性组件：Spawn(from, to, color) 后自己推进 0.18s 并销毁。
//  · 朝向 = 出手者 → 目标；长度 ≈ 两点距离 × 1.05；高度按长度比例；
//  · `_Progress` 用真实时间推进（读数型演出，不吃倍速压缩）；
//  · 材质不可用（shader 缺失）时**静默不生成**，绝不留下白块。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class SlashFx : MonoBehaviour
    {
        private Material _mat;
        private SpriteRenderer _sr;
        private float _t;
        private float _life = 0.18f;
        private float _fadeFrom = 0.12f;
        private Transform _follow;      // 出手者（弧光跟着"往前顶"一起走）
        private Vector3 _toWorld;       // 目标点（世界坐标，冲锋落点）

        /// <summary>在世界坐标 from → to 之间甩一道弧光。</summary>
        public static void Spawn(Vector3 from, Vector3 to, Color color, float life = 0.18f,
                                 Transform follow = null)
        {
            var sh = Resources.Load<Shader>("Battle/SlashArc");
            var sp = Resources.Load<Sprite>("Battle/FX_slash_arc");
            if (sh == null || sp == null || !sh.isSupported) return;

            var go = new GameObject("SlashFx");
            var fx = go.AddComponent<SlashFx>();
            fx.Build(from, to, color, life, sh, sp, follow);
        }

        private void Build(Vector3 from, Vector3 to, Color color, float life, Shader sh, Sprite sp,
                           Transform follow)
        {
            _follow = follow;
            _toWorld = to;
            _life = Mathf.Max(0.08f, life);

            Vector2 d = to - from;
            float len = Mathf.Max(0.5f, d.magnitude * 1.35f);
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            transform.position = from;
            transform.rotation = Quaternion.Euler(0f, 0f, ang);

            _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sprite = sp;
            _sr.sortingOrder = 32;

            // 尺寸：长度对齐两点距离，高度 = 长度 × 0.55（弧的弯曲是贴图里的）
            var b = sp.bounds.size;
            transform.localScale = new Vector3(len / Mathf.Max(0.001f, b.x), (len * 0.50f) / Mathf.Max(0.001f, b.y), 1f);

            _mat = new Material(sh);
            _mat.SetColor("_Color", color);
            _mat.SetFloat("_Progress", 0f);
            _mat.SetFloat("_Soft", 0.18f);
            _mat.SetFloat("_EdgeBoost", 0.9f);
            _mat.SetFloat("_Alpha", 1f);
            _sr.material = _mat;
        }

        private void Update()
        {
            if (_mat == null || _sr == null) { Destroy(gameObject); return; }
            _t += Time.deltaTime;                       // 真实时间：演出不吃倍速

            // ★ 跟着出手者走：异兽往前顶，弧光跟着顶过去（用户 2026-10-24）
            if (_follow != null)
            {
                Vector3 from = _follow.position;
                Vector2 dir = (Vector2)(_toWorld - from);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    transform.position = from;
                    transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                    var b = _sr.bounds.size;   // 注意：bounds 已是世界尺寸，这里用贴图原始尺寸换算
                    var spb = _sr.sprite != null ? _sr.sprite.bounds.size : new Vector3(1f, 1f, 0f);
                    float len = Mathf.Max(0.5f, dir.magnitude * 1.35f);
                    transform.localScale = new Vector3(len / Mathf.Max(0.001f, spb.x),
                                                       (len * 0.50f) / Mathf.Max(0.001f, spb.y), 1f);
                }
            }

            float k = Mathf.Clamp01(_t / _life);
            _mat.SetFloat("_Progress", Mathf.SmoothStep(0f, 1f, k));

            float a = 1f;
            if (k > 0.55f) a = Mathf.Lerp(1f, 0f, (k - 0.55f) / 0.45f);
            _mat.SetFloat("_Alpha", a);

            if (k >= 1f)
            {
                Destroy(_mat);
                Destroy(gameObject);
            }
        }
    }
}
