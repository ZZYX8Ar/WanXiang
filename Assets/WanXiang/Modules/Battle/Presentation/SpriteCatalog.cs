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
            public Sprite Body;     // 立绘（透明 PNG，基线 y=88%）—— 战场用，1024
            public Sprite Head;     // UI 头像（256，已打图集）—— 列表/格子/预览用
        }

        public List<Entry> Entries = new List<Entry>(32);
        private Dictionary<string, Sprite> _map;
        private Dictionary<string, Sprite> _headMap;

        /// <summary>战场立绘（1024）。UI 列表里别用它 —— 一张就是一次纹理切换。</summary>
        public Sprite Get(string id)
        {
            if (_map == null || _map.Count != Entries.Count)
            {
                _map = new Dictionary<string, Sprite>(Entries.Count);
                foreach (var e in Entries)
                    if (!string.IsNullOrEmpty(e.Id) && e.Body != null)
                        _map[e.Id] = e.Body;
            }
            if (id != null && _map.TryGetValue(id, out var s)) return s;
            // ★ 融合体 Id =「宿主Id+灵魂Id」（FusionRules.Fuse），目录里只有宿主 Id。
            //   截取 "+" 前的宿主 Id 再查一次 —— 否则所有融合体都掉进 BattleStage2D
            //   的「按序号分配」兜底（Player 取前5张/Enemy 取后5张），
            //   表现就是：名字对、立绘错、两侧立绘还不一样（用户实测）。
            if (id != null)
            {
                int plus = id.IndexOf('+');
                if (plus > 0 && _map.TryGetValue(id.Substring(0, plus), out s)) return s;
            }
            return null;
        }

        /// <summary>
        /// UI 头像（256，同图集）。列表 / 卡片 / 小格子一律用它：
        /// 30 只异兽共享一张图集纹理，一屏下来立绘只占 1 个 DC，
        /// 否则 30 张 1024 散图就是 30 个 DC。
        /// Head 没生成时自动回退到 Body（缺资源不炸）。
        /// </summary>
        public Sprite GetHead(string id)
        {
            if (_headMap == null || _headMap.Count != Entries.Count)
            {
                _headMap = new Dictionary<string, Sprite>(Entries.Count);
                foreach (var e in Entries)
                {
                    if (string.IsNullOrEmpty(e.Id)) continue;
                    _headMap[e.Id] = e.Head != null ? e.Head : e.Body;
                }
            }
            if (id != null && _headMap.TryGetValue(id, out var s)) return s;
            // ★ 同 Get：融合体 Id 带「+」，按宿主 Id 再查（头像同理）。
            if (id != null)
            {
                int plus = id.IndexOf('+');
                if (plus > 0 && _headMap.TryGetValue(id.Substring(0, plus), out s)) return s;
            }
            return null;
        }

        public void Rebuild()
        {
            _map = null;
            _headMap = null;
            Get(null);
        }
    }
}
