// ============================================================================
//  EntranceSpark —— Boss 登场"元素异象"（2026-10-25）
//  ---------------------------------------------------------------------------
//  cut-in 之外再撒一层**方向性粒子**，让 25 个首领按五行有辨识度：
//   木：藤蔓自下窜起（向上、带摆）      火：火星上腾 + 抖
//   金：横向刃风扫过                    水：逆流上涌
//   土：自上压落 + 尘土
//  纯表现，不改战斗数据；0.6s 内自毁。
//  ⚠ 复用现有 `Battle/FX_motion_fog` 贴图，不新增美术。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed class EntranceSpark : MonoBehaviour
    {
        private struct P { public Transform T; public Vector3 V; public float T0; public SpriteRenderer R; }
        private readonly List<P> _list = new List<P>(28);
        private const float Life = 0.65f;

        public static void Burst(Vector3 center, Element el, Color color, int count = 18)
        {
            var sp = Resources.Load<Sprite>("Battle/FX_motion_fog");
            if (sp == null) return;

            var go = new GameObject("EntranceSpark");
            go.transform.position = center;
            var fx = go.AddComponent<EntranceSpark>();

            for (int i = 0; i < count; i++)
            {
                var c = new GameObject("p" + i);
                c.transform.SetParent(go.transform, false);
                c.transform.position = center + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.25f, 0.35f), 0f);
                var sr = c.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = 21;
                sr.color = new Color(color.r, color.g, color.b, 0.0f);

                Vector3 v;
                float spd = Random.Range(1.2f, 2.8f);
                switch (el)
                {
                    case Element.Wood:  v = new Vector3(Random.Range(-0.35f, 0.35f), spd, 0f); break;   // 藤蔓窜起
                    case Element.Fire:  v = new Vector3(Random.Range(-0.2f, 0.2f), spd * 1.25f, 0f); break;
                    case Element.Metal: v = new Vector3((Random.value > 0.5f ? 1f : -1f) * spd * 1.6f, Random.Range(-0.15f, 0.15f), 0f); break;
                    case Element.Water: v = new Vector3(Random.Range(-0.15f, 0.15f), spd * 1.1f, 0f); break;
                    case Element.Earth: v = new Vector3(Random.Range(-0.5f, 0.5f), -spd * 1.15f, 0f); break;
                    default:            v = new Vector3(Random.Range(-0.4f, 0.4f), spd * 0.8f, 0f); break;
                }

                var b = sp.bounds.size;
                float s = Random.Range(0.18f, 0.42f);
                c.transform.localScale = new Vector3(s / Mathf.Max(0.001f, b.x), s / Mathf.Max(0.001f, b.y), 1f);

                fx._list.Add(new P { T = c.transform, V = v, T0 = 0f, R = sr });
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var p = _list[i];
                if (p.T == null) { _list.RemoveAt(i); continue; }
                p.T0 += dt;
                p.T.position += p.V * dt;
                p.V += new Vector3(0f, -1.2f * dt, 0f);          // 轻微重力，落地感
                float k = Mathf.Clamp01(p.T0 / Life);
                var c = p.R.color; c.a = Mathf.Lerp(0.85f, 0f, k); p.R.color = c;
                if (k >= 1f) { Destroy(p.T.gameObject); _list.RemoveAt(i); }
            }
            if (_list.Count == 0) Destroy(gameObject);
        }
    }
}
