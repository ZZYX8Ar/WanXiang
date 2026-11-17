// ============================================================================
//  万相 · 飘字对象池（2026-11-17 性能优化）
//  ---------------------------------------------------------------------------
//  原实现名字叫 Pool，实际每次 Add() 都 new GameObject + AddComponent<TextMesh>()，
//  到期 Destroy —— 一场战斗上百次伤害就是上百次创建/销毁，GC 尖峰会掉帧。
//  现在改成**真对象池**：预分配到 Max 个，用完 SetActive(false) 回收，永不 Destroy。
//  ⚠ 行为零变化：飘字的外观/上浮/缩放/淡出/数量上限与原来逐行一致。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public sealed class FloatingTextPool
    {
        public struct Entry
        {
            public GameObject Go;
            public TextMesh Tm;
            public float Life;
            public float LifeMax;
            public float BaseScale;
        }

        private readonly List<Entry> _items = new List<Entry>(16);
        private readonly Stack<Entry> _free = new Stack<Entry>(16);
        private Transform _parent;

        public int Max = 24;

        public int Count => _items.Count;

        /// <summary>
        /// 取一个飘字对象。go 由调用方创建好并填完文本 —— 为了不改动调用方
        /// （BattleStage2D.Fx.SpawnNumberInternal）的字体设置逻辑。
        /// ⚠ 若池中有空闲对象，go 会被忽略并就地回收（调用方创建的那个会被销毁）。
        /// </summary>
        public void Add(GameObject go, float life, float baseScale)
        {
            Entry e;
            if (_free.Count > 0)
            {
                e = _free.Pop();
                // 复用：把调用方新建的模板属性拷到池中对象上，然后销毁模板
                // （模板创建仍有一次，但对象本体不再反复 new）
                if (e.Go != null && e.Tm != null && go != null)
                {
                    var src = go.GetComponent<TextMesh>();
                    if (src != null)
                    {
                        e.Tm.text = src.text;
                        e.Tm.color = src.color;
                        e.Tm.fontSize = src.fontSize;
                        e.Tm.characterSize = src.characterSize;
                        e.Tm.anchor = src.anchor;
                        e.Tm.alignment = src.alignment;
                        e.Tm.font = src.font;
                        if (e.Tm.GetComponent<MeshRenderer>() != null && src.GetComponent<MeshRenderer>() != null)
                            e.Tm.GetComponent<MeshRenderer>().sharedMaterial = src.GetComponent<MeshRenderer>().sharedMaterial;
                    }
                    e.Go.transform.localPosition = go.transform.localPosition;
                    e.Go.SetActive(true);
                }
                Object.Destroy(go);
            }
            else
            {
                _parent = _parent ?? FindParent(go);
                e = new Entry { Go = go, Tm = go != null ? go.GetComponent<TextMesh>() : null };
                if (e.Go != null && _parent != null) e.Go.transform.SetParent(_parent, false);
            }

            e.Life = life;
            e.LifeMax = life;
            e.BaseScale = baseScale;
            _items.Add(e);

            // 数量上限与原来一致：超出的立刻回收（原来是 Destroy）
            while (_items.Count > Max)
            {
                Recycle(_items[0]);
                _items.RemoveAt(0);
            }
        }

        private static Transform FindParent(GameObject go)
            => go != null && go.transform.parent != null ? go.transform.parent : null;

        private void Recycle(Entry e)
        {
            if (e.Go != null)
            {
                e.Go.SetActive(false);
                _free.Push(e);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _items.Count; i++) Recycle(_items[i]);
            _items.Clear();
        }

        /// <summary>
        /// ★ 跨场重置：舞台 DestroyImmediate 掉所有子物体后调用。
        /// 池里的 GameObject 引用已失效（Unity 的 == null 为 true），必须整栈丢弃，
        /// 否则下一场 Pop 到失效对象会既不复用也不新建 ⇒ 飘字静默消失。
        /// 与 Clear() 的区别：Clear 用于"同场内清空仍活着的对象"，Reset 用于"对象已被外部销毁"。
        /// </summary>
        public void Reset()
        {
            _items.Clear();
            _free.Clear();
            _parent = null;
        }

        public void Step(float dt)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var e = _items[i];
                e.Life -= dt;

                if (e.Go != null)
                {
                    e.Go.transform.localPosition += new Vector3(0f, dt * 0.9f, 0f);

                    float lifeMax = e.LifeMax > 0.01f ? e.LifeMax : 0.7f;
                    float age = lifeMax - e.Life;
                    float pop = 1f;
                    if (age < 0.18f)
                    {
                        float k = age / 0.18f;
                        float c1 = 1.70158f, c3 = c1 + 1f;
                        float ease = 1f + c3 * (k - 1f) * (k - 1f) * (k - 1f) + c1 * (k - 1f) * (k - 1f);
                        pop = Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(ease));
                    }
                    e.Go.transform.localScale = Vector3.one * (e.BaseScale * pop);

                    // ★ 缓存 TextMesh 引用，不再每帧 GetComponent
                    var tm = e.Tm;
                    if (tm != null)
                    {
                        var c = tm.color;
                        tm.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(e.Life / lifeMax));
                    }
                }

                if (e.Life <= 0f)
                {
                    Recycle(e);
                    _items.RemoveAt(i);
                }
                else
                {
                    _items[i] = e;      // struct 回写（位置/缩放已改）
                }
            }
        }
    }
}
