// ============================================================================
//  万相 · 立绘目录（SpriteCatalog）
//  ---------------------------------------------------------------------------
//  id → Sprite 的运行时查询表。SpriteImporter 扫描 ArtRes/Units 后填充。
//  BeastDef.Id 即查询键（jumang / guanguan / ...），**不需要**在 BeastDef 上加
//  引用字段 —— 内容导入器生成 BeastDef，美术导入器生成 Sprite，两边用 id 对齐。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    [CreateAssetMenu(fileName = "SpriteCatalog", menuName = "万相/立绘目录 SpriteCatalog")]
    public sealed class SpriteCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string Id;       // BeastDef.Id（如 jumang）
            public Sprite Body;     // 立绘（透明 PNG，基线 y=88%）
        }

        public List<Entry> Entries = new List<Entry>(32);
        private Dictionary<string, Sprite> _map;

        public Sprite Get(string id)
        {
            if (_map == null || _map.Count != Entries.Count)
            {
                _map = new Dictionary<string, Sprite>(Entries.Count);
                foreach (var e in Entries)
                    if (!string.IsNullOrEmpty(e.Id) && e.Body != null)
                        _map[e.Id] = e.Body;
            }
            return id != null && _map.TryGetValue(id, out var s) ? s : null;
        }

        public void Rebuild()
        {
            _map = null;
            Get(null);
        }
    }
}
