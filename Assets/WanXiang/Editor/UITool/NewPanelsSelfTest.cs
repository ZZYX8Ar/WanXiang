// ============================================================================
//  万相 · 编辑器工具 · 新面板结构自检（B2/B3 UI）
//  ---------------------------------------------------------------------------
//  S1/S2/S4 的三个新面板用 Find 取控件（prefab 与脚本解耦），
//  **没有 SerializeField 绑定可拖错**，但"节点存在性"仍可自动兜底：
//    本自检加载 prefab → 按规格清单核对节点 → 根组件存在性 → 报告。
//
//  新增面板/节点时，在 Checklists 里补一行即可（表驱动，加面板不改逻辑）。
//  报告：Temp/WanXiangDiag/newpanels_selftest.txt
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class NewPanelsSelfTest
    {
        private const string ReportPath = "Temp/WanXiangDiag/newpanels_selftest.txt";

        /// <summary>面板 → （根组件类型, 必需节点清单）。</summary>
        private static readonly Dictionary<string, (string rootType, string[] nodes)> Checklists
            = new Dictionary<string, (string, string[])>
        {
            ["Assets/Resources/UI/Panel_Inspiration.prefab"] = (
                "WanXiang.Modules.UI.InspirationPanel",
                new[]
                {
                    "Root_TopBar/Tmp_Title", "Root_TopBar/Tmp_Points", "Root_TopBar/Btn_Close",
                    "Branch_travel/Tmp_BranchName", "Branch_travel/Grid/Btn_T1", "Branch_travel/Grid/Btn_T5",
                    "Branch_battle/Tmp_BranchName", "Branch_battle/Grid/Btn_T5",
                    "Branch_forge/Tmp_BranchName", "Branch_forge/Grid/Btn_T5",
                }),
            ["Assets/Resources/UI/Panel_SquadSelect.prefab"] = (
                "WanXiang.Modules.UI.SquadSelectPanel",
                new[]
                {
                    "Root_TopBar/Tmp_Title", "Root_TopBar/Tmp_SubTitle", "Root_TopBar/Btn_Close",
                    "Root_Difficulty/Btn_Minus", "Root_Difficulty/Tmp_Level",
                    "Root_Difficulty/Btn_Plus", "Root_Difficulty/Tmp_Rule",
                    "Scroll_Grid/Viewport/Content",
                    "Scroll_Grid/Viewport/Content/Row_Squad_Template",
                    "Scroll_Grid/Viewport/Content/Tmp_Empty",
                    "Root_Bottom/Tmp_TeamPreview", "Root_Bottom/Btn_Start",
                    "Root_Bottom/Btn_CopyCode", "Root_Bottom/Btn_ImportCode",   // B5-1 挑战分享码
                }),
            ["Assets/Resources/UI/Panel_DaoMark.prefab"] = (
                "WanXiang.Modules.UI.DaoMarkPanel",
                new[]
                {
                    "Root_TopBar/Tmp_Title", "Root_TopBar/Tmp_Count", "Root_TopBar/Btn_Close",
                    "Scroll_Grid/Viewport/Content",
                    "Scroll_Grid/Viewport/Content/Row_Dao_Template",
                    "Root_Right/Tmp_DetailName", "Root_Right/Tmp_DetailStar",
                    "Root_Right/Tmp_DetailDesc", "Root_Right/Tmp_DetailSource",
                    "Root_Right/Btn_Equip",
                    "Root_Right/Root_Equipped/Slot_0/Tmp_SlotName",
                    "Root_Right/Root_Equipped/Slot_1/Tmp_SlotName",
                    "Root_Share/Tmp_Code", "Root_Share/Btn_Copy", "Root_Share/Btn_Import",
                }),
            ["Assets/Resources/UI/Panel_SkillLoadout.prefab"] = (
                "WanXiang.Modules.UI.SkillLoadoutPanel",
                new[]
                {
                    "Root_TopBar/Tmp_Title", "Root_TopBar/Btn_Close",
                    "Scroll_Beasts/Viewport/Content",
                    "Scroll_Beasts/Viewport/Content/Row_Beast_Template",
                    "Root_Right/Tmp_BeastName",
                    "Root_Right/Slot_0/Tmp_SlotType", "Root_Right/Slot_0/Tmp_SkillName",
                    "Root_Right/Slot_1/Tmp_SlotType", "Root_Right/Slot_1/Tmp_SkillName",
                    "Root_Right/Slot_1/Btn_Change",
                    "Root_Right/Slot_2/Tmp_SkillName",
                    "Root_Right/Slot_3/Tmp_SkillName",
                    "Root_Right/Tmp_Hint",
                    "Root_Picker/Scroll_Pick/Viewport/Content",
                    "Root_Picker/Scroll_Pick/Viewport/Content/Row_Skill_Template",
                    "Root_Picker/Btn_Cancel",
                }),
        };

        [MenuItem("万相/UI/新面板结构自检")]
        public static void RunFromMenu() => Run("newpanels.selftest");

        public static string[] Run(string command)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("万相 · 新面板结构自检");
            sb.AppendLine(new string('=', 72));
            int pass = 0, fail = 0;

            foreach (var kv in Checklists)
            {
                sb.AppendLine("── " + kv.Key);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kv.Key);
                if (prefab == null) { fail++; sb.AppendLine("  ❌ prefab 缺失"); continue; }

                var rootType = Type.GetType(kv.Value.rootType + ", WanXiang.Modules.UI");
                var hasComponent = rootType != null && prefab.GetComponent(rootType) != null;
                if (hasComponent) { pass++; sb.AppendLine("  ✅ 根组件 " + kv.Value.rootType); }
                else { fail++; sb.AppendLine("  ❌ 根组件缺失：" + kv.Value.rootType); }

                foreach (var path in kv.Value.nodes)
                {
                    var t = prefab.transform.Find(path);
                    if (t != null) { pass++; sb.AppendLine("  ✅ " + path); }
                    else { fail++; sb.AppendLine("  ❌ 缺节点：" + path); }
                }
            }

            sb.AppendLine(new string('=', 72));
            sb.AppendLine("结论：" + pass + " 项通过，" + fail + " 项失败");
            var report = sb.ToString();
            try
            {
                System.IO.Directory.CreateDirectory("Temp/WanXiangDiag");
                System.IO.File.WriteAllText(ReportPath, report);
            }
            catch { /* Temp 不可写时只走控制台 */ }
            Debug.Log(report);
            return report.Split('\n');
        }
    }
}
