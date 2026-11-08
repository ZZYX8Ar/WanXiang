// ============================================================================
//  万相 · 爆闪池（表现层重构 R3-c）
//  ---------------------------------------------------------------------------
//  从 BattleStage2D 抽出的**格挡爆闪**：圆环/圆盘从 0 快速放大到 R1 并淡出。
//  与 FloatingTextPool 同模式：struct 单列表 + Spawn/Step/ClearRefs。
//
//  ⛔ 不认识格挡语义（颜色/半径/时长全由调用方给）——Stage2D 决定"放什么"。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class BurstPool
    {
        private struct Item
        {
            public SpriteRenderer Sr;
            public float T, Dur, R0, R1;
            public Color C;
        }

        private readonly List<Item> _items = new List<Item>(8);

        public int Count => _items.Count;

        public void Spawn(Transform parent, Vector3 pos, Sprite sp, Color c,
                          float r0, float r1, float dur, int order)
        {
            var go = new GameObject("ParryBurst");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            if (sp != null && sp.texture != null) sp.texture.wrapMode = TextureWrapMode.Repeat;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = order;
            sr.color = c;
            _items.Add(new Item { Sr = sr, T = 0f, Dur = dur, R0 = r0, R1 = r1, C = c });
        }

        /// <summary>逐帧推进（easeOut 放大 + 淡出，到期 Destroy）。数学与重构前逐行一致。</summary>
        public void Step(float dt)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var b = _items[i];
                if (b.Sr == null) { _items.RemoveAt(i); continue; }
                b.T += dt;
                float k = Mathf.Clamp01(b.T / b.Dur);
                float e = 1f - (1f - k) * (1f - k);
                float r = Mathf.Lerp(b.R0, b.R1, e);
                b.Sr.transform.localScale = new Vector3(r, r, 1f);
                var c = b.C;
                b.Sr.color = new Color(c.r, c.g, c.b, c.a * (1f - k));
                if (k >= 1f)
                {
                    Object.Destroy(b.Sr.gameObject);
                    _items.RemoveAt(i);
                }
                else
                {
                    _items[i] = b;
                }
            }
        }

        /// <summary>只清引用（舞台 Clear 时调用；子物体已被统一销毁）。</summary>
        public void ClearRefs() => _items.Clear();
    }
}
