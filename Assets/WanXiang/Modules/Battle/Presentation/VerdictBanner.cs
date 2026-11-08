// ============================================================================
//  万相 · 判定横幅（表现层重构 R3-a）
//  ---------------------------------------------------------------------------
//  从 BattleStage2D 抽出的**屏幕中央结果横幅**（完美格挡/共鸣/阵成…）：
//    弹入(easeOutBack 1.55×) → 停留 0.62s → 淡出；底衬按文字宽度自适应。
//
//  · 全部状态（TextMesh/Renderer/底衬/时长/透明度）自持 ⇒ Stage2D 不再背 6 个字段
//  · Ensure(parent, font)：首次调用时创建节点（惰性，与重构前一致）
//  · 底衬 sprite 用 StageSpriteFactory.Solid（与 QTE 提示条同一来源）
//  ⛔ 不做判定、不认识格挡/共鸣语义 —— Stage2D 决定"什么时候放什么字"。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class VerdictBanner
    {
        private const float Hold = 0.62f;          // 停留时长（与重构前一致）
        private const float BgAlpha = 0.86f;

        private TextMesh _text;
        private MeshRenderer _mr;
        private SpriteRenderer _bg;
        private float _t;
        private float _alpha;
        private bool _active;

        public bool Visible => _alpha > 0.01f;

        /// <summary>惰性创建（父节点 = 舞台根；字体 = LegacyRuntime，与重构前一致）。</summary>
        public void Ensure(Transform parent)
        {
            if (_text != null) return;

            var bgGo = new GameObject("VerdictBg");
            bgGo.transform.SetParent(parent, false);
            bgGo.transform.localPosition = new Vector3(0f, 2.05f, -3f);
            _bg = bgGo.AddComponent<SpriteRenderer>();
            _bg.sprite = StageMaterialFactory.Solid(64);
            _bg.sortingOrder = 41;

            var go = new GameObject("Verdict");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 2.05f, -2f);
            var f = LegacyFont();
            _text = go.AddComponent<TextMesh>();
            _mr = go.GetComponent<MeshRenderer>();
            if (f != null)
            {
                _text.font = f;
                if (_mr != null) { _mr.sharedMaterial = f.material; _mr.sortingOrder = 42; }
            }
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.characterSize = 0.17f;    // 比 QTE 提示条大一倍：这是"结果"，不是"操作提示"
            _text.fontSize = 64;
        }

        public void Show(string text, Color c, Transform parent)
        {
            Ensure(parent);
            if (_text == null) return;
            _text.text = text;
            _text.color = c;
            if (_bg != null) _bg.color = new Color(c.r * 0.22f, c.g * 0.22f, c.b * 0.22f, BgAlpha);
            _t = 0f;
            _active = true;
        }

        /// <summary>逐帧推进（弹入/停留/淡出 + 底衬自适应）。数学与重构前逐行一致。</summary>
        public void Step(float dt)
        {
            if (_text == null) return;
            if (!_active)
            {
                if (_alpha <= 0.01f) return;
                _alpha = Mathf.MoveTowards(_alpha, 0f, dt * 6f);
            }
            else
            {
                _t += dt;
                if (_t >= Hold)
                {
                    _active = false;
                    _alpha = Mathf.MoveTowards(_alpha, 0f, dt * 4.5f);
                }
                else _alpha = Mathf.MoveTowards(_alpha, 1f, dt * 12f);
            }
            var vc = _text.color;
            _text.color = new Color(vc.r, vc.g, vc.b, _alpha);
            if (_mr != null) _mr.enabled = _alpha > 0.01f;

            float scale = 1f;
            if (_active && _t < 0.16f)
            {
                float k = Mathf.Clamp01(_t / 0.16f);
                float c1 = 1.70158f, c3 = c1 + 1f;
                float e = 1f + c3 * (k - 1f) * (k - 1f) * (k - 1f) + c1 * (k - 1f) * (k - 1f);
                scale = Mathf.Lerp(1.55f, 1f, Mathf.Clamp01(e));
            }
            _text.transform.localScale = new Vector3(scale, scale, 1f);

            if (_bg != null)
            {
                var bc = _bg.color;
                _bg.color = new Color(bc.r, bc.g, bc.b, BgAlpha * _alpha);
                _bg.enabled = _alpha > 0.01f;
                if (_mr != null && _alpha > 0.01f)
                {
                    float tw = _mr.bounds.size.x;
                    float ps = _bg.transform.parent != null ? _bg.transform.parent.lossyScale.x : 1f;
                    if (ps > 0.0001f) tw /= ps;
                    float w = Mathf.Clamp(tw > 0.01f ? tw + 0.55f : 2.4f, 1.6f, 9.0f);
                    _bg.transform.localScale = new Vector3(w, 0.72f, 1f);
                }
            }
        }

        /// <summary>只清引用（舞台 Clear 时调用；子物体已被统一销毁，不 Destroy）。</summary>
        public void ClearRefs()
        {
            _text = null; _mr = null; _bg = null;
            _active = false; _alpha = 0f; _t = 0f;
        }

        private static Font _legacy;
        private static Font LegacyFont()
        {
            if (_legacy != null) return _legacy;
            _legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_legacy == null) _legacy = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _legacy;
        }
    }
}
