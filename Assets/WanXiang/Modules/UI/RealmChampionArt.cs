// ============================================================================
//  RealmChampionArt —— 秘境镇守立绘（2026-10-22 用户提供 10 张透明立绘）
//  ---------------------------------------------------------------------------
//  资源位置：Resources/UI/Realm/Champion/<镇守名>.png（1024²，透明底）
//  文件名必须与 RealmCatalog 里 RealmChampionDef.Name **完全一致**（含「·」）。
//
//  用法：var sp = RealmChampionArt.Get(champ);   // 取不到返回 null，不挡流程
//  ⚠ 放在 UI 程序集：Campaign.Core 是纯逻辑，不引用 UnityEngine。
// ============================================================================
using UnityEngine;

namespace WanXiang.Campaign
{
    public static class RealmChampionArt
    {
        private const string Root = "UI/Realm/Champion/";

        public static Sprite Get(string championName)
        {
            if (string.IsNullOrEmpty(championName)) return null;
            return Resources.Load<Sprite>(Root + championName);
        }

        public static Sprite Get(RealmChampionDef def)
            => def == null ? null : Get(def.Name);
    }
}
