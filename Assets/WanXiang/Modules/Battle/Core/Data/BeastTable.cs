// ============================================================================
//  万相 · 异兽身份表 & 通用标签表（骨架 / B0）
//  ---------------------------------------------------------------------------
//  ⚠ 本文件在 Battle.Core（noEngineReferences）：**只提供 Install**，不碰 TextAsset。
//     CSV 解析在 UI 层 `Modules/UI/DataTables.cs`，解析完调 Install。
//
//  设计目标：
//    · 加/删一只异兽 = 加/删一行 CSV，代码零改动
//    · 表不存在 / 行为空 ⇒ 自动用内置默认，不抛异常、不改旧玩法
//    · 显示名通过 LabelTable 注入，新增节点/品级/性情名称不用改 switch
// ============================================================================
using System;
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>一只异兽的"身份卡"（不替代 BeastDef，只是配置来源）。</summary>
    public sealed class BeastProfile
    {
        public string Id;
        public string Name;
        public Element Element;
        public RoleType Role;
        public Rarity Rarity;
        public Temperament Temperament;
        public int TierScore;                 // 品级考据得分（v2 §1）

        public int Hp, Atk, Def, Spd;         // 面板固定值（不再按稀有度乘）

        public string TraitId;
        public TraitKind TraitKind;
        public float TraitValue, TraitValue2;
        public string TraitName;
        public string Ultimate;
    }

    public static class BeastTable
    {
        private static readonly Dictionary<string, BeastProfile> _byId =
            new Dictionary<string, BeastProfile>(StringComparer.Ordinal);
        private static readonly List<BeastProfile> _all = new List<BeastProfile>();

        public static bool Installed { get; private set; }
        public static IReadOnlyList<BeastProfile> All => _all;

        /// <summary>整体替换（清掉旧表）。空列表 = 回退到内置定义，不报错。</summary>
        public static void Install(IReadOnlyList<BeastProfile> rows)
        {
            _byId.Clear();
            _all.Clear();
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var p = rows[i];
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    _byId[p.Id] = p;
                    _all.Add(p);
                }
            }
            Installed = _all.Count > 0;
        }

        public static bool TryGet(string id, out BeastProfile p)
        {
            if (string.IsNullOrEmpty(id)) { p = null; return false; }
            return _byId.TryGetValue(id, out p);
        }

        /// <summary>重载 / 测试用。</summary>
        public static void Clear() { _byId.Clear(); _all.Clear(); Installed = false; }
    }

    /// <summary>
    /// 通用显示名表：group 可以是 "node" / "tier" / "temper" / "trait"…
    /// ⚠ 找不到就返回调用方给的 fallback —— 永远不因为缺表而崩。
    /// </summary>
    public static class LabelTable
    {
        private static readonly Dictionary<string, string> _labels =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string Key(string group, string id) => (group ?? "") + "/" + (id ?? "");

        public static void Install(string group, string id, string name)
        {
            if (string.IsNullOrEmpty(id)) return;
            _labels[Key(group, id)] = name ?? id;
        }

        public static void Clear() => _labels.Clear();

        public static string Of(string group, string id, string fallback = null)
        {
            string v;
            if (_labels.TryGetValue(Key(group, id), out v) && !string.IsNullOrEmpty(v)) return v;
            return fallback ?? id;
        }
    }
}
