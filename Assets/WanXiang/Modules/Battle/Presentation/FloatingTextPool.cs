// ============================================================================
//  万相 · 飘字池（表现层重构 R4）
//  ---------------------------------------------------------------------------
//  从 BattleStage2D 抽出：原来用 **4 个并行 List**（_tempTexts/_tempLife/_tempLifeMax/
//  _tempBaseScale）靠下标对齐，任何一处漏删/漏插就会错位（寿命与缩放串到别的字上）。
//
//  现在合并为 `Entry` 单列表 + 独立类：
//    · Add(go, life, baseScale)   入池（超上限自动回收最旧）
//    · Step(dt)                   上浮 / pop 缩放 / 淡出 / 到期销毁
//    · Clear()                    只清引用（与重构前一致，不 Destroy）
//    · Max 可调（默认 24，与原来一致）
//
//  ⛔ 只负责"一串数字的生命周期"，不创建 GameObject、不碰字体/排序层（那些留在 Stage2D）。
//  ⛔ 动画数学与重构前逐行一致，改手感请改这里一处。
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
            public float Life;
            public float LifeMax;
            public float BaseScale;
        }

        private readonly List<Entry> _items = new List<Entry>(16);

        /// <summary>同时存在的上限（超出回收最旧），默认与原实现一致。</summary>
        public int Max = 24;

        public int Count => _items.Count;

        public void Add(GameObject go, float life, float baseScale)
        {
            _items.Add(new Entry { Go = go, Life = life, LifeMax = life, BaseScale = baseScale });

            // 超出上限：回收最旧（延迟销毁，禁用 DestroyImmediate）
            while (_items.Count > Max)
            {
                var first = _items[0];
                if (first.Go != null) Object.Destroy(first.Go);
                _items.RemoveAt(0);
            }
        }

        public void Clear() => _items.Clear();

        /// <summary>逐帧推进。数学与重构前 BattleStage2D 的飘字循环一致。</summary>
        public void Step(float dt)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var e = _items[i];
                e.Life -= dt;

                if (e.Go != null)
                {
                    // 上浮
                    e.Go.transform.localPosition += new Vector3(0f, dt * 0.9f, 0f);

                    // 弹出：起手 1.5× 快速回落（easeOutBack），暴击基准更大
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

                    var tm = e.Go.GetComponent<TextMesh>();
                    if (tm != null)
                    {
                        var c = tm.color;
                        tm.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(e.Life / lifeMax));
                    }
                }

                if (e.Life <= 0f)
                {
                    // 每帧路径：必须用 Destroy（延迟），DestroyImmediate 会随战斗变长越卡越死
                    if (e.Go != null) Object.Destroy(e.Go);
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
