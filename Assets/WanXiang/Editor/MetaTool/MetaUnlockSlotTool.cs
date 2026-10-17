// ============================================================================
//  万相 · 调试工具：指定槽位「全解锁」（2026-10-17）
//  ---------------------------------------------------------------------------
//  用途：验证主界面动态立绘 / 残卷阁领取链路，不必真的去集 50×6 张碎片。
//  做的事（直接改槽位文件，绕过 MetaStore 的槽位状态机）：
//    · 50 只异兽剧情碎片全部 6/6（FragmentCounts 顶满）
//    · 觉醒材料全部已领取（AwakenSouls）
//    · 动态立绘全部解锁（LivePortraits）+ 看板兽 = jumang
//    · 宿主/灵魂解锁池全开
//  ⛔ 只准 Edit 模式跑（Play 模式下内存态会立刻把文件盖回去）。
//  用法：菜单 万相/调试/存档/三号槽全解锁 → 启动游戏选 3 号槽。
// ============================================================================

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class MetaUnlockSlotTool
    {
        private const int TargetSlot = 3;
        private const string PreferredPortrait = "jumang";

        [MenuItem("万相/调试/存档/三号槽全解锁", priority = 220)]
        internal static void Run()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[UnlockSlot] Play 模式禁止执行：内存态会把文件盖回去。先退出 Play。");
                return;
            }

            string path = Path.Combine(Application.persistentDataPath, "wanxiang_meta_s" + TargetSlot + ".sav");

            // 读旧档（v6/v7 都认）；没有就给个新档壳
            WanXiang.Meta.MetaState st = null;
            if (File.Exists(path))
            {
                if (WanXiang.Meta.MetaSaveCode.TryDecode(File.ReadAllText(path), out var loaded))
                    st = loaded;
                else
                    Debug.LogWarning("[UnlockSlot] 三号槽旧档解码失败（版本不匹配？），按新建处理");
            }
            if (st == null) st = new WanXiang.Meta.MetaState(20260922UL);

            // 内容目录 → 全部异兽 id
            var cats = Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (cats == null || cats.Length == 0) { Debug.LogError("[UnlockSlot] 找不到 ContentCatalogSO"); return; }
            var beasts = WanXiang.Fusion.ContentLibrary.BuildBeasts(cats[0]);
            if (beasts == null || beasts.Length == 0) { Debug.LogError("[UnlockSlot] 内容目录里没有异兽"); return; }

            int fragN = 0, soulN = 0, liveN = 0;
            for (int i = 0; i < beasts.Length; i++)
            {
                var b = beasts[i];
                if (b == null) continue;
                while (st.FragmentCountOf(b.Id) < WanXiang.Meta.MetaState.FragmentTotal)
                    st.AddFragment(b.Id);                       // 上限 FragmentTotal，while 必然终止
                if (!st.HasAwakenSoul(b.Id)) { st.ClaimAwakenSoul(b.Id); soulN++; }
                if (!st.HasLivePortrait(b.Id)) { st.ForceUnlockLivePortrait(b.Id); liveN++; }
                fragN++;
            }
            st.HomePortraitId = PreferredPortrait;

            // 宿主/灵魂解锁池全开（「全解锁」顺手做满；孵化计数不动，避免扰动孵蛋随机流）
            for (int i = 0; i < beasts.Length; i++)
            {
                if (!st.UnlockedHosts.Contains(i)) st.UnlockedHosts.Add(i);
                if (!st.UnlockedSouls.Contains(i)) st.UnlockedSouls.Add(i);
            }

            string code = WanXiang.Meta.MetaSaveCode.Encode(st);
            if (string.IsNullOrEmpty(code)) { Debug.LogError("[UnlockSlot] 编码失败（字段超限？）"); return; }
            File.WriteAllText(path, code);

            Debug.Log("[UnlockSlot] 三号槽全解锁完成：" + path + "\n"
                      + "  异兽 " + fragN + " 只：碎片 6/6、觉醒材料 +" + soulN + "、动态立绘 +" + liveN
                      + "，看板兽 = " + PreferredPortrait
                      + "，宿主/灵魂解锁 " + st.UnlockedHosts.Count + "/" + st.UnlockedSouls.Count);
        }
    }
}
#endif
