// ============================================================================
//  BossRushCatalog —— 「首领试炼」可选 Boss 清单（2026-10-25）
//  ---------------------------------------------------------------------------
//  用户要一个主界面的「Boss 连战/单挑」入口，方便逐个测试 25 只首领的技能。
//  清单来源（不复制数据，全部现查现建）：
//   · 主线：`BossCatalog.Pool`（按幕分组，每幕 3 / 3 / 3 / 3 / 2 = 14）
//   · 秘境：`RealmCatalog` 里各场域的 `Champions`（10 条）
//
//  ⚠ 秘境首领目前**只有名字与机制钩子**，没有独立的 BeastDef / 立绘，
//    它们的战斗体由 `RealmMapBuilder` 在秘境节点里按场域 + 钩子构建。
//    ⇒ v1 先把它们列出来（标 `Playable=false`），下一阶段接进战斗。
// ============================================================================
using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Modules.UI
{
    public struct BossRushEntry
    {
        public string Id;          // b_xxx（主线）
        public string Name;
        public Element Element;
        public int Act;            // 1..5
        public string RealmId;     // 非空 = 秘境首领
        public string RealmName;   // 秘境名（显示用）
        public string HookId;      // 秘境机制钩子（rc_*）
        public string ChampionName; // 秘境首领本名（不含场域后缀），用来查立绘
        public bool Playable;      // false = 本阶段还不能直接开战
        public string Epithet;     // 一句人设（秘境镇守用）
        public string Mechanic;    // 机制文案（详情面板展示）
    }

    public static class BossRushCatalog
    {
        private static List<BossRushEntry> _all;

        public static List<BossRushEntry> All
        {
            get
            {
                if (_all != null) return _all;
                _all = new List<BossRushEntry>(28);

                // ---- 主线：按幕池 ----
                var pool = BossCatalog.Pool;
                for (int act = 0; act < pool.Length; act++)
                {
                    foreach (var id in pool[act])
                    {
                        var def = BossCatalog.Get(id);
                        if (def == null) continue;
                        _all.Add(new BossRushEntry
                        {
                            Id = id,
                            Name = def.DisplayName,
                            Element = def.Element,
                            Act = act + 1,
                            Playable = true,
                        });
                    }
                }

                // ---- 秘境首领 ----
                var realms = WanXiang.Campaign.RealmCatalog.All;
                if (realms != null)
                {
                    foreach (var realm in realms)
                    {
                        if (realm == null || realm.Champions == null) continue;
                        foreach (var ch in realm.Champions)
                        {
                            if (ch == null) continue;
                            _all.Add(new BossRushEntry
                            {
                                Id = null,
                                Name = ch.Name + "（" + realm.Name + "）",
                                Element = ElementOfRealm(realm.Element),
                                Act = 0,
                                RealmId = realm.Id,
                                RealmName = realm.Name,
                                HookId = ch.HookId,
                            ChampionName = ch.Name,
                                Epithet = ch.Epithet,
                                Mechanic = ch.MechanicText,
                                Playable = true,        // v2：用同属性 Boss 作战斗体 + 场域钩子
                            });
                        }
                    }
                }
                return _all;
            }
        }

        private static Element ElementOfRealm(string elementName)
        {
            switch (elementName)
            {
                case "木": return Element.Wood;
                case "火": return Element.Fire;
                case "金": return Element.Metal;
                case "水": return Element.Water;
                case "土": return Element.Earth;
                default: return Element.None;
            }
        }

        /// <summary>按幕分组标题。</summary>
        public static string GroupTitle(BossRushEntry e)
        {
            if (!string.IsNullOrEmpty(e.RealmId)) return "秘境 · " + e.RealmName;
            switch (e.Act)
            {
                case 1: return "第一幕 · 春";
                case 2: return "第二幕 · 夏";
                case 3: return "第三幕 · 秋";
                case 4: return "第四幕 · 冬";
                default: return "第五幕 · 长夏";
            }
        }
    }
}
