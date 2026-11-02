// ============================================================================
//  WanXiang · 数据表校验（编辑器）
//  ---------------------------------------------------------------------------
//  菜单：万相 / 配置 / 校验所有 CSV
//
//  作用：重新解析 Resources/Data 下的 7 张表并打印结果。
//  能抓到的错：列名拼错、id 为空、首领 id 不存在、枚举写错、数值越界、
//             天时原子编码段数不足、地图层数 < 4 —— 都会带「文件 + 行号」。
//
//  ⛔ 它**不会**替你判断"数值好不好玩"，只保证"表能读进来且语义没坏"。
// ============================================================================

using UnityEditor;
using UnityEngine;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    public static class CsvSelfTest
    {
        [MenuItem("万相/配置/校验所有 CSV", priority = 211)]
        public static void ValidateAll()
        {
            DataTables.ReloadForEditor();
            int problems = DataTables.Problems.Count;

            string summary =
                "状态 " + DataTables.StatusCount +
                "／首领 " + DataTables.BossCount +
                "／天时 " + DataTables.WeatherCount +
                "／预算 " + DataTables.BudgetKeys + " 项" +
                "／规模 " + DataTables.SquadActs + " 幕" +
                "／地图 " + (DataTables.MapInstalled ? "已装" : "内置") +
                "／节点权重 " + DataTables.WeightRows + " 行";

            if (problems == 0)
                Debug.Log("[CsvSelfTest] ✅ 通过：" + summary);
            else
                Debug.LogError("[CsvSelfTest] ❌ " + problems + " 处问题：" + summary + "\n" +
                               string.Join("\n", DataTables.Problems.ToArray()));
        }
    }
}
