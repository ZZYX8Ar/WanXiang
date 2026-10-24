// ============================================================================
//  HitFlash —— 命中"短促白闪"（2026-10-24）
//  ---------------------------------------------------------------------------
//  用户："出手的打击感 短促白闪 这个可以试试"
//
//  做法：一层**相机空间**的全屏白 Sprite（不是 UI，避免 raycast / Canvas 排序问题），
//  命中瞬间 alpha 冲到 0.10~0.16，然后在 **0.07 秒**内线性归零并自毁。
//   · 真实时间推进（不吃倍速），否则 4 倍速下只剩 0.02s；
//   · 全局冷却 0.12s：连击/群攻时不会变成频闪；
//   · sortingOrder 380（在单位/伤害数字之上、大招 cut-in 遮罩 450 之下）。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class HitFlash : MonoBehaviour
    {
        private static float _nextAllowed;

        private SpriteRenderer _sr;
        private float _a0;
        private float _t;
        private const float Life = 0.07f;

        /// <summary>闪一下。alpha 建议 0.10（普通）/ 0.16（暴击）。</summary>
        public static void Flash(float alpha, Camera cam = null)
        {
            if (alpha <= 0.001f) return;
            if (Time.unscaledTime < _nextAllowed) return;      // 冷却：防频闪
            _nextAllowed = Time.unscaledTime + 0.12f;

            cam = cam != null ? cam : Camera.main;
            float h = cam != null && cam.orthographic ? cam.orthographicSize : 5.4f;
            float w = h * (cam != null ? cam.aspect : 1.78f);

            var go = new GameObject("HitFlash");
            var fx = go.AddComponent<HitFlash>();
            fx._sr = go.AddComponent<SpriteRenderer>();
            fx._sr.sprite = WhiteSprite();
            fx._sr.sortingOrder = 380;
            fx._sr.color = new Color(1f, 1f, 1f, alpha);
            fx._a0 = alpha;

            Vector3 center = cam != null ? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f) : Vector3.zero;
            go.transform.position = new Vector3(center.x, center.y, -1f);
            var b = fx._sr.sprite.bounds.size;
            go.transform.localScale = new Vector3(w * 2.2f / Mathf.Max(0.001f, b.x),
                                                   h * 2.2f / Mathf.Max(0.001f, b.y), 1f);
        }

        private static Sprite _white;
        private static Sprite WhiteSprite()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _white;
        }

        private void Update()
        {
            if (_sr == null) { Destroy(gameObject); return; }
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / Life);
            var c = _sr.color;
            c.a = Mathf.Lerp(_a0, 0f, k);
            _sr.color = c;
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
