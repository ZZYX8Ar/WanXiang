// ============================================================================
//  BossIntroFx —— 首领登场排版层（2026-10-26，明日方舟 / AE PV 风格）
//  ---------------------------------------------------------------------------
//  ⛔ 这是**独立一套**，不碰 `SkillFxPlayer` 的终结技 cut-in。
//  版式（左侧竖排，右侧留给立绘）：
//      ⬥ 冰 渊 之 主            ← 元素色小字（字距拉开）
//      玄 溟                    ← 大字（滑入，不缩放）
//      ───────────────          ← 元素色规线（从左展开）
//      HP 1400 · 速度 90        ← 数据小字
//  入场：整块从左侧滑入 + 规线展开；同时画面压暗。1.5s 后整体淡出。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class BossIntroFx : MonoBehaviour
    {
        private SpriteRenderer _dim;
        private TextMesh _tag, _name, _stat;
        private SpriteRenderer _rule;
        private Transform _block;
        private float _t;
        private const float Life = 1.55f;
        private const float InDur = 0.22f;

        private static Font _font;
        private static Font LegacyFont()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        private static TextMesh MakeText(Transform parent, string name, string text, float size,
                                         Color color, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            var f = LegacyFont();
            var mr = go.GetComponent<MeshRenderer>();
            if (f != null) { tm.font = f; if (mr != null) mr.sharedMaterial = f.material; }
            if (mr != null) mr.sortingOrder = 521;
            tm.text = text;
            tm.characterSize = size;
            tm.fontSize = 64;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Left;
            tm.color = color;
            return tm;
        }

        public static void Play(Vector3 bossPos, string tag, string bossName, string stats, Color color)
        {
            var go = new GameObject("BossIntroFx");
            var fx = go.AddComponent<BossIntroFx>();

            var cam = Camera.main;
            float h = cam != null && cam.orthographic ? cam.orthographicSize : 5.4f;
            float w = h * (cam != null ? cam.aspect : 1.78f);
            Vector3 camC = cam != null ? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f) : Vector3.zero;

            // ① 全屏压暗
            var dimGo = new GameObject("Dim");
            dimGo.transform.SetParent(go.transform, false);
            var bgSp = Resources.Load<Sprite>("Battle/BG_act1_spring");
            var sr = dimGo.AddComponent<SpriteRenderer>();
            sr.sprite = bgSp; sr.sortingOrder = 30; sr.color = new Color(0f, 0f, 0f, 0f);
            if (bgSp != null)
            {
                var b = bgSp.bounds.size;
                dimGo.transform.localScale = new Vector3(w * 2.4f / Mathf.Max(0.001f, b.x),
                                                         h * 2.4f / Mathf.Max(0.001f, b.y), 1f);
            }
            dimGo.transform.position = camC;
            fx._dim = sr;

            // ② 文本块：放在画面左侧 32% 处
            var block = new GameObject("Block");
            block.transform.SetParent(go.transform, false);
            float x = camC.x - w * 0.60f;
            float y = camC.y + h * 0.22f;
            block.transform.position = new Vector3(x, y, 0f);
            fx._block = block.transform;
            fx._startX = block.transform.localPosition.x;

            var tagTm = MakeText(block.transform, "Tag", tag, 0.10f,
                                 new Color(color.r, color.g, color.b, 1f), TextAnchor.UpperLeft);
            tagTm.transform.localPosition = Vector3.zero;

            var nameTm = MakeText(block.transform, "Name", bossName, 0.20f,
                                  new Color(1f, 0.98f, 0.94f, 1f), TextAnchor.UpperLeft);
            nameTm.transform.localPosition = new Vector3(0f, -0.30f, 0f);

            var ruleGo = new GameObject("Rule");
            ruleGo.transform.SetParent(block.transform, false);
            var rule = ruleGo.AddComponent<SpriteRenderer>();
            rule.sprite = Resources.Load<Sprite>("Battle/FX_motion_glow");
            rule.sortingOrder = 519;
            rule.color = new Color(color.r, color.g, color.b, 0.9f);
            ruleGo.transform.localPosition = new Vector3(0f, -0.46f, 0f);
            fx._rule = rule;

            var statTm = MakeText(block.transform, "Stat", stats, 0.085f,
                                  new Color(0.86f, 0.83f, 0.76f, 1f), TextAnchor.UpperLeft);
            statTm.transform.localPosition = new Vector3(0f, -0.60f, 0f);

            fx._tag = tagTm; fx._name = nameTm; fx._stat = statTm;
            block.transform.localPosition += new Vector3(-w * 0.35f, 0f, 0f);   // 起始位置：左侧画外
        }

        private void Update()
        {
            _t += Time.deltaTime;

            // 压暗：0→0.72（0.16s），0.9s 后缓出
            if (_dim != null)
            {
                float a = _t < 0.16f ? Mathf.Lerp(0f, 0.72f, _t / 0.16f)
                                     : Mathf.Lerp(0.72f, 0f, Mathf.Clamp01((_t - 0.90f) / 0.65f));
                var c = _dim.color; c.a = a; _dim.color = c;
            }

            // 文本块滑入（OutCubic），并保持
            if (_block != null)
            {
                float k = Mathf.Clamp01(_t / InDur);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                var lp = _block.localPosition;
                lp.x = Mathf.Lerp(_startX - 6.5f, _startX, e);
                _block.localPosition = lp;

                // 规线展开
                if (_rule != null && _rule.sprite != null)
                {
                    float rk = Mathf.Clamp01((_t - 0.10f) / 0.30f);
                    float full = 2.4f;
                    var b = _rule.sprite.bounds.size;
                    _rule.transform.localScale = new Vector3(full * rk / Mathf.Max(0.001f, b.x),
                                                             0.014f / Mathf.Max(0.001f, b.y), 1f);
                }

                // 整块淡出
                float fade = _t > Life - 0.45f ? Mathf.Clamp01((Life - _t) / 0.45f) : 1f;
                SetAlpha(_tag, fade); SetAlpha(_name, fade); SetAlpha(_stat, fade);
                if (_rule != null) { var c = _rule.color; c.a = 0.9f * fade; _rule.color = c; }
            }

            if (_t >= Life) Destroy(gameObject);
        }

        private float _startX = 0f;

        private static void SetAlpha(TextMesh tm, float a)
        {
            if (tm == null) return;
            var c = tm.color; c.a = a; tm.color = c;
        }
    }
}
