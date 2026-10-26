// ============================================================================
//  UnitInspectCard —— 战斗内单位信息卡（2026-10-26，崩铁式"双击查看"）
//  ---------------------------------------------------------------------------
//  用户："双击怪物弹面板，看它当前状态和基本战斗信息；小怪也要"
//  做法：世界空间一张 `panel_dialog` 底板 + 若干 TextMesh，贴在单位旁；
//        再点一次别的单位会替换，6 秒自动消失。
//  ⚠ 数据全部来自**帧快照**（UnitSnapshot），不读 BattleState（自动模式下那是终局态）。
// ============================================================================
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class UnitInspectCard : MonoBehaviour
    {
        private static UnitInspectCard _current;
        private float _t;
        private const float Life = 6f;
        private TextMesh[] _lines;
        private SpriteRenderer _plate;

        public static void Show(Vector3 worldAnchor, string[] lines, Color tint)
        {
            Hide();
            var go = new GameObject("UnitInspectCard");
            var card = go.AddComponent<UnitInspectCard>();

            // 底板
            var plateGo = new GameObject("Plate");
            plateGo.transform.SetParent(go.transform, false);
            var sr = plateGo.AddComponent<SpriteRenderer>();
            sr.sprite = Resources.Load<Sprite>("UI/Kit/panel_dialog");
            sr.sortingOrder = 640;
            sr.color = new Color(1f, 0.99f, 0.95f, 0.96f);

            float lineH = 0.16f;
            float h = lines.Length * lineH + 0.22f;
            float w = 2.9f;
            if (sr.sprite != null)
            {
                var b = sr.sprite.bounds.size;
                plateGo.transform.localScale = new Vector3(w / Mathf.Max(0.001f, b.x), h / Mathf.Max(0.001f, b.y), 1f);
            }

            // 文字
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            card._lines = new TextMesh[lines.Length];
            float top = h * 0.5f - 0.10f;
            for (int i = 0; i < lines.Length; i++)
            {
                var tg = new GameObject("L" + i);
                tg.transform.SetParent(go.transform, false);
                var tm = tg.AddComponent<TextMesh>();
                var mr = tg.GetComponent<MeshRenderer>();
                if (font != null) { tm.font = font; if (mr != null) mr.sharedMaterial = font.material; }
                if (mr != null) mr.sortingOrder = 641;
                tm.text = lines[i];
                tm.characterSize = i == 0 ? 0.075f : 0.055f;
                tm.fontSize = 64;
                tm.anchor = TextAnchor.UpperLeft;
                tm.alignment = TextAlignment.Left;
                tm.color = i == 0 ? new Color(0.16f, 0.13f, 0.09f) : new Color(0.28f, 0.24f, 0.18f);
                tg.transform.localPosition = new Vector3(-w * 0.5f + 0.14f, top - i * lineH, -0.01f);
                card._lines[i] = tm;
            }

            // 标题条（元素色）
            var barGo = new GameObject("Bar");
            barGo.transform.SetParent(go.transform, false);
            var bar = barGo.AddComponent<SpriteRenderer>();
            bar.sprite = Resources.Load<Sprite>("Battle/FX_motion_glow");
            bar.sortingOrder = 641;
            bar.color = new Color(tint.r, tint.g, tint.b, 0.9f);
            if (bar.sprite != null)
            {
                var bb = bar.sprite.bounds.size;
                barGo.transform.localScale = new Vector3(w * 0.92f / Mathf.Max(0.001f, bb.x), 0.012f / Mathf.Max(0.001f, bb.y), 1f);
            }
            barGo.transform.localPosition = new Vector3(0f, h * 0.5f - 0.15f, -0.02f);

            go.transform.position = worldAnchor + new Vector3(1.75f, 0.55f, -0.3f);
            card._plate = sr;
            _current = card;
        }

        public static void Hide()
        {
            if (_current != null) { Destroy(_current.gameObject); _current = null; }
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float a = _t > Life - 0.4f ? Mathf.Clamp01((Life - _t) / 0.4f) : 1f;
            if (_plate != null) { var c = _plate.color; c.a = 0.96f * a; _plate.color = c; }
            if (_lines != null)
                for (int i = 0; i < _lines.Length; i++)
                    if (_lines[i] != null) { var c = _lines[i].color; c.a = a; _lines[i].color = c; }
            if (_t >= Life) { if (_current == this) _current = null; Destroy(gameObject); }
        }
    }
}
