// ============================================================================
//  BossIntroFx —— 首领登场"压迫感"层（2026-10-25）
//  ---------------------------------------------------------------------------
//  用户："只看到冲击波和两条黄线，没有压迫感和冲击力"。
//  这里补的是**场级**的三件事（与 cut-in 并行，不替代）：
//   ① 全屏压暗：0 → 0.78 → 0（快进慢出），把画面按下去；
//   ② 名字砸下：世界空间大字从 2.2 倍缩到 1.0 带过冲，随后淡出；
//   ③ 镜头：由调用方配合 TriggerZoom / TriggerShake。
//  纯表现，0.9s 内自毁。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class BossIntroFx : MonoBehaviour
    {
        private SpriteRenderer _dim;
        private TextMesh _name;
        private Transform _nameT;
        private float _t;
        private const float Life = 1.15f;

        public static void Play(Vector3 center, string text, Color color, float worldHeight)
        {
            var go = new GameObject("BossIntroFx");
            var fx = go.AddComponent<BossIntroFx>();

            // ① 全屏压暗（相机空间，跟着相机）
            var dimGo = new GameObject("Dim");
            dimGo.transform.SetParent(go.transform, false);
            var sp = Resources.Load<Sprite>("Battle/BG_act1_spring");   // 只用其 mesh
            var sr = dimGo.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = 30;
            sr.color = new Color(0f, 0f, 0f, 0f);
            var cam = Camera.main;
            float h = cam != null && cam.orthographic ? cam.orthographicSize : 5.4f;
            float w = h * (cam != null ? cam.aspect : 1.78f);
            var b = sp != null ? sp.bounds.size : new Vector3(1f, 1f, 0f);
            dimGo.transform.localScale = new Vector3(w * 2.4f / Mathf.Max(0.001f, b.x),
                                                     h * 2.4f / Mathf.Max(0.001f, b.y), 1f);
            if (cam != null) dimGo.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
            fx._dim = sr;

            // ② 名字砸下
            var nameGo = new GameObject("Name");
            nameGo.transform.SetParent(go.transform, false);
            nameGo.transform.position = center + new Vector3(0f, worldHeight * 0.55f, -0.5f);
            // ⛔ 名字不在这里画：TextMesh 用内置 Arial，中文会变口口。
            //   名字由 cut-in（TMP + Deng SDF）负责，这里只做压暗 + 镜头。
            nameGo.SetActive(false);
            fx._name = null;
            fx._nameT = null;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Life);

            if (_dim != null)
            {
                // 0→0.78 在 0.18s 内压下去，之后缓出
                float a = _t < 0.18f ? Mathf.Lerp(0f, 0.78f, _t / 0.18f)
                                     : Mathf.Lerp(0.78f, 0f, Mathf.Clamp01((_t - 0.55f) / 0.55f));
                var c = _dim.color; c.a = a; _dim.color = c;
            }

            if (_nameT != null)
            {
                // 2.2 → 1.0 带过冲（0.22s 冲过头到 0.92，再回 1.0）
                float s;
                if (_t < 0.16f) s = Mathf.Lerp(2.2f, 0.94f, _t / 0.16f);
                else if (_t < 0.30f) s = Mathf.Lerp(0.94f, 1.02f, (_t - 0.16f) / 0.14f);
                else s = Mathf.Lerp(1.02f, 1.0f, Mathf.Clamp01((_t - 0.30f) / 0.20f));
                _nameT.localScale = Vector3.one * s;

                if (_name != null)
                {
                    var c = _name.color;
                    c.a = _t < 0.75f ? 1f : Mathf.Lerp(1f, 0f, (_t - 0.75f) / 0.40f);
                    _name.color = c;
                }
            }

            if (k >= 1f) Destroy(gameObject);
        }
    }
}
