// ============================================================================
//  万相 · QTE 提示条（表现层重构 R3-b，重做版）
//  ---------------------------------------------------------------------------
//  从 BattleStage2D 抽出的**屏幕中央按键提示条**（"按 J 格挡！""格挡（2/3）"）：
//    窗口期常亮（text 非空 → alpha 淡入到 1），底衬按文字宽度自适应。
//
//  · 全部状态自持 ⇒ Stage2D 不再背 5 个字段
//  · 底衬 sprite 用 StageMaterialFactory.Solid（与判定横幅共用，深色染色）
//  ⛔ 不做判定、不知道 QTE 档位 —— Stage2D 决定"什么时候提示什么"。
//  ⚠ 重做记录：第一次抽取因正则删 Ensure 方法体匹配到类闭合括号而失败回滚；
//    本次 Stage2D 侧全部改为**行号定位**替换。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class QtePrompt
    {
        private TextMesh _text;
        private MeshRenderer _mr;
        private SpriteRenderer _bg;
        private float _alpha;
        private string _current = "";

        public bool Visible => _alpha > 0.01f;

        /// <summary>设置提示文案（多段攻击自动拼连击计数）；首次调用时创建节点。</summary>
        public void Set(string text, int hitsTotal, int hitsDone, Transform parent)
        {
            _current = hitsTotal > 1
                ? text + "　（连击 " + (hitsDone + 1) + "/" + hitsTotal + "）"
                : text;
            Ensure(parent);
            if (_text != null) _text.text = _current;
        }

        /// <summary>逐帧推进（窗口期常亮淡入 + 底衬自适应）。数学与重构前逐行一致。</summary>
        public void Step(float dt)
        {
            if (_text == null) return;
            float targetA = string.IsNullOrEmpty(_current) ? 0f : 1f;
            _alpha = Mathf.MoveTowards(_alpha, targetA, dt * 6f);
            var c = _text.color;
            _text.color = new Color(c.r, c.g, c.b, _alpha);
            if (_mr != null) _mr.enabled = _alpha > 0.01f;
            if (_bg != null)
            {
                var bc = _bg.color;
                _bg.color = new Color(bc.r, bc.g, bc.b, 0.72f * _alpha);
                _bg.enabled = _alpha > 0.01f;
                if (_mr != null && _alpha > 0.01f)
                {
                    float w = 1.2f;
                    float tw = _mr.bounds.size.x;
                    float ps = _bg.transform.parent != null ? _bg.transform.parent.lossyScale.x : 1f;
                    if (ps > 0.0001f) tw /= ps;
                    if (tw > 0.01f) w = tw + 0.30f;
                    w = Mathf.Clamp(w, 1.2f, 8.0f);
                    _bg.transform.localScale = new Vector3(w, 0.46f, 1f);
                }
            }
        }

        /// <summary>S4：清空当前文字（下一帧淡出；节点保留）。连击段间调用。</summary>
        public void ClearCurrent() { _current = ""; }

        /// <summary>只清引用（舞台 Clear 时调用；子物体已被统一销毁，不 Destroy）。</summary>
        public void ClearRefs()
        {
            _text = null; _mr = null; _bg = null;
            _alpha = 0f; _current = "";
        }

        private void Ensure(Transform parent)
        {
            if (_text != null || parent == null) return;

            var bgGo = new GameObject("QteHintBg");
            bgGo.transform.SetParent(parent, false);
            bgGo.transform.localPosition = new Vector3(0f, 1.72f, -3f);
            _bg = bgGo.AddComponent<SpriteRenderer>();
            _bg.sprite = StageMaterialFactory.Solid(64);
            _bg.sortingOrder = 39;
            _bg.color = new Color(0.06f, 0.05f, 0.07f, 0.72f);

            var go = new GameObject("QteHint");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.72f, -2f);
            var f = LegacyFont();
            _text = go.AddComponent<TextMesh>();
            _mr = go.GetComponent<MeshRenderer>();
            if (f != null)
            {
                _text.font = f;
                if (_mr != null) { _mr.sharedMaterial = f.material; _mr.sortingOrder = 40; }
            }
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.characterSize = 0.10f;
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
