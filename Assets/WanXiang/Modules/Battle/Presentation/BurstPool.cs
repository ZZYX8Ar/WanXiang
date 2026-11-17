// ============================================================================
//  万相 · 火花对象池（2026-11-17 性能优化）
//  ---------------------------------------------------------------------------
//  原实现每次 Spawn 都 new GameObject + AddComponent<SpriteRenderer>()，
//  到期 Destroy —— 格挡火花/元素爆发高频触发时会产生 GC 尖峰。
//  现在改成**真对象池**：预分配复用，到期 SetActive(false) 回收，永不 Destroy。
//  ⚠ 行为零变化：缩放曲线(1-(1-k)^2)/淡出/sortingOrder 与原来逐行一致。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class BurstPool
    {
        private struct Item
        {
            public GameObject Go;
            public SpriteRenderer Sr;
            public float T, Dur, R0, R1;
            public Color C;
        }

        private readonly List<Item> _items = new List<Item>(8);
        private readonly Stack<Item> _free = new Stack<Item>(8);

        public int Count => _items.Count;

        public void Spawn(Transform parent, Vector3 pos, Sprite sp, Color c,
                          float r0, float r1, float dur, int order)
        {
            if (sp != null && sp.texture != null) sp.texture.wrapMode = TextureWrapMode.Repeat;

            Item b;
            if (_free.Count > 0)
            {
                b = _free.Pop();
                if (b.Go != null)
                {
                    b.Go.SetActive(true);
                    b.Go.transform.SetParent(parent, false);
                    b.Go.transform.position = pos;
                }
                if (b.Sr != null)
                {
                    b.Sr.sprite = sp;
                    b.Sr.sortingOrder = order;
                    b.Sr.color = c;
                }
            }
            else
            {
                var go = new GameObject("ParryBurst");
                go.transform.SetParent(parent, false);
                go.transform.position = pos;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = order;
                sr.color = c;
                b = new Item { Go = go, Sr = sr };
            }
            b.T = 0f; b.Dur = dur; b.R0 = r0; b.R1 = r1; b.C = c;
            _items.Add(b);
        }

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
                    Recycle(b);
                    _items.RemoveAt(i);
                }
                else
                {
                    _items[i] = b;
                }
            }
        }

        private void Recycle(Item b)
        {
            if (b.Go == null) return;
            b.Go.SetActive(false);
            _free.Push(b);
        }

        // ★ 原来只清列表（对象留在场景 active）；现在所有活跃对象一并回收。
        public void ClearRefs()
        {
            for (int i = 0; i < _items.Count; i++) Recycle(_items[i]);
            _items.Clear();
        }
    }
}
