// ============================================================================
//  万相 · 遗物图目录（RelicSpriteCatalog）
//  ---------------------------------------------------------------------------
//  RelicDef.Id → Sprite 的运行时查询表（结构对齐 SpriteCatalog）。
//  美术来源：Assets/ArtRes/Relics/<中文图名>.png（由 ArtPipelineTool 扫描灌入）。
//  解析规则 + 别名表都在 ArtPipelineTool.FillRelicSpriteCatalog 里，
//  这里只负责"按 id 取图" + 运行时兜底加载。
//
//  ⚠ 本文件放在 Modules/UI（引用 UnityEngine 的程序集），不要移到 Campaign/Core——
//    Core 是不引用 UnityEngine 的纯核心程序集，ScriptableObject / Sprite 编译不过。
//
//  运行时兜底：资产放在 Assets/Resources/RelicSpriteCatalog.asset，保证进包且
//  Resources.Load 稳定命中（SpriteCatalog 靠 prefab 引用进包，遗物图没那么重引用）。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Campaign
{
    [CreateAssetMenu(fileName = "RelicSpriteCatalog", menuName = "万相/遗物图目录 RelicSpriteCatalog")]
    public sealed class RelicSpriteCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string Id;       // RelicDef.Id（如 r_mark_jumang / r_foe_wood_1）
            public Sprite Sprite;   // 遗物图标（透明 PNG）
        }

        public List<Entry> Entries = new List<Entry>(128);
        private Dictionary<string, Sprite> _map;

        /// <summary>按遗物 id 取图标；查不到返回 null（调用方应回退到稀有度色块/文字）。</summary>
        public Sprite Get(string id)
        {
            if (_map == null || _map.Count != Entries.Count)
            {
                _map = new Dictionary<string, Sprite>(Entries.Count);
                foreach (var e in Entries)
                    if (!string.IsNullOrEmpty(e.Id) && e.Sprite != null)
                        _map[e.Id] = e.Sprite;
            }
            if (id != null && _map.TryGetValue(id, out var s)) return s;
            return null;
        }

        public void Rebuild()
        {
            _map = null;
            Get(null);
        }

        // ================================================================
        //  运行时兜底：资产放在 Assets/Resources/RelicSpriteCatalog.asset
        // ================================================================

        private static RelicSpriteCatalog _inst;

        public static RelicSpriteCatalog Find()
        {
            if (_inst != null) return _inst;
            _inst = Resources.Load<RelicSpriteCatalog>("RelicSpriteCatalog");
            return _inst;
        }

        /// <summary>仅供编辑器工具在填充后刷新缓存用。</summary>
        public static void InvalidateCache() => _inst = null;
    }
}
