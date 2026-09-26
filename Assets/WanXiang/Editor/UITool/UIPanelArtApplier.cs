// ============================================================================
//  WanXiang · 面板美术应用器
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / UI / 应用美术资源到面板
//
//  做的事：把 Assets/ArtRes 下处理好的 Sprite 按「节点名 → 美术 key」的规则挂到
//  14 个面板预制体的 Image 上，九宫格件自动切 Sliced + Border。
//
//  为什么用规则表而不是逐个手拖：
//    · 14 个界面 500+ 个 Image，手拖既慢又容易漏；
//    · 规则表写在代码里，改了图重跑一遍即可，不会出现"某张图忘了换"；
//    · 节点名本来就是契约（见《UGUI 拼装规范》第 3 章），用它当 key 名正言顺。
//  幂等：反复执行结果一致。重新生成结构（生成面板预制体）之后要再跑一次本菜单。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class UIPanelArtApplier
    {
        private const string PrefabDir = "Assets/Resources/UI";
        private const string Parts = "Assets/ArtRes/UI/Parts/";
        private const string Screens = "Assets/ArtRes/Screens/";
        private const string BattleBg = "Assets/ArtRes/BattleBg/";

        /// <summary>没有面板概念稿的面板 → 用哪张图当底图。
        /// 开始界面没有单独出图，用「天阙 · 远景云台层」——云海加悬浮石台，中间大面积留空，正合标题画面。</summary>
        private static readonly Dictionary<string, string> ScreenFallback = new Dictionary<string, string>
        {
            { "Panel_Start", BattleBg + "bg_tianque_far.png" },
            { "Panel_Save", BattleBg + "bg_tianque_far.png" },
        };

        /// <summary>精确节点名 → 美术 key。</summary>
        private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>
        {
            { "Img_Plate", "@screen" },
            { "Img_TopBg", "panel_dialog" },
            { "Img_BoardBg", "board" },
            { "Img_CellHighlight", "board_cell" },
            { "Img_TitleScroll", "panel_title" },
            { "Img_Banner", "banner_win" },
            { "Img_RewardEgg", "currency_egg" },
            { "Img_Vortex", "cd_ring_bg2" },
            { "Img_SoulPreview", "currency_ink" },
            { "Img_Sw_3", "cd_ring_fill" },
            { "Img_RageRingBg", "cd_ring_bg" },
            { "Img_RageRing", "cd_ring_fill" },
            { "Img_HpBg", "bar_hp" },
            { "Img_HpFill", "bar_hp2" },
            { "Img_RealmBadge", "mark_boss" },
            { "Img_WeatherIcon", "refresh_icon" },
            { "Img_NodeIcon", "refresh_icon" },
            { "Img_Seal", "sold_seal" },
            { "Img_Dot", "currency_egg" },
            { "Img_GoodsSold", "sold_seal" },
            { "Tmp_InputShare", "listitem" },
            { "Root_Gain", "panel_dialog" },
            { "Root_Cost", "panel_dialog" },
            { "Root_SideMenu", "panel_dialog" },
            { "Root_TeamPreview", "panel_dialog" },
            { "Root_BottomBar", "panel_dialog" },
            { "Root_EnemyIntel", "panel_dialog" },
            { "Root_Roster", "panel_dialog" },
            { "Root_Hosts", "panel_dialog" },
            { "Root_Souls", "panel_dialog" },
            { "Root_Goods", "panel_dialog" },
            { "Root_Market", "panel_dialog" },
            { "Root_Detail", "panel_dialog" },
            { "Root_Audio", "panel_dialog" },
            { "Root_Video", "panel_dialog" },
            { "Root_Keybinds", "panel_dialog" },
            // 原 Root_Share / Root_SaveLoad 已删（分享码归 Panel_Pvp、存取档归 Panel_Save）⇒ 映射一并去掉
            { "Root_Log", "toast" },
            { "Root_Tracks", "panel_dialog" },
            { "Img_TrackLine", "divider" },
            { "Img_BondRow", "divider" },
            { "Img_GoodsSold_0", "sold_seal" },
            { "Img_GoodsSold_1", "sold_seal" },
            { "Img_GoodsSold_2", "sold_seal" },
            { "Img_GoodsSold_3", "sold_seal" },
            { "Img_GoodsSold_4", "sold_seal" },
            { "Img_GoodsSold_5", "sold_seal" },
        };

        /// <summary>按前缀匹配（后写的不覆盖先写的，先命中先用）。</summary>
        private static readonly (string prefix, string key)[] ByPrefix =
        {
            ("Btn_Deploy", "btn_primary"), ("Btn_Start", "btn_primary"), ("Btn_Confirm", "btn_primary"),
            ("Btn_Fuse", "btn_primary"), ("Btn_Import", "btn_primary"), ("Btn_Next", "btn_primary"),
            ("Btn_Accept", "btn_primary"), ("Btn_Ultimate", "btn_primary"),
            ("Btn_", "btn_secondary"),
            ("Img_GoodsHead_", "frame_avatar_legend"),
            ("Img_EnemyAvatar_", "frame_avatar_epic"),
            ("Img_Avatar_", "frame_avatar_rare"),
            ("Img_Head", "frame_avatar_rare"),
            ("Img_Bond_0", "bond_wood_fire"), ("Img_Bond_1", "bond_fire_earth"),
            ("Img_Bond_2", "bond_earth_metal"), ("Img_Bond_3", "bond_metal_water"),
            ("Img_Bond_4", "bond_water_wood"),
            ("Img_Bond_5", "resonance_2"), ("Img_Bond_6", "resonance_4"), ("Img_Bond_7", "resonance_5"),
            ("Item_Roster", "listitem"), ("Item_Seal", "listitem"),
            ("Item_Key", "listitem"), ("Item_Reward", "slot_card"),
            ("Draft_", "slot_card"), ("Goods_", "slot_card"), ("Choice_", "slot_card"),
            ("Opt_", "listitem"),
        };

        /// <summary>保留原色（不要刷成白色）的节点前缀 —— 它们的颜色本身有语义。</summary>
        private static readonly string[] KeepColor =
        { "Img_EnemyAvatar_", "Img_Bond_", "Img_Sw_", "Img_Cover_", "Img_GoodsSold", "Img_HpFill", "Img_RageRing" };

        /// <summary>保留原填充类型（Filled）的节点 —— 不能被改成 Sliced。</summary>
        private static readonly string[] KeepFill = { "Img_HpFill", "Img_RageRing", "Img_Sw_3" };

        [MenuItem("WanXiang/UI/应用美术资源到面板", priority = 102)]
        public static void ApplyAll()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });
            int panels = 0, images = 0, missing = 0;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var file = Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith("Panel_")) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var panelName = file;                     // Panel_Home
                    var screenSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Screens + panelName + ".png");
                    if (screenSprite == null && ScreenFallback.TryGetValue(panelName, out var fb))
                        screenSprite = AssetDatabase.LoadAssetAtPath<Sprite>(fb);
                    var all = root.GetComponentsInChildren<Image>(true);
                    int touched = 0;

                    foreach (var img in all)
                    {
                        string key = Resolve(img.gameObject.name, out bool fromScreen);
                        string assetPath = fromScreen ? null : Parts + key + ".png";
                        Sprite sprite = fromScreen ? screenSprite
                                                   : AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                        if (sprite == null)
                        {
                            if (key != null) missing++;
                            continue;
                        }

                        img.sprite = sprite;
                        if (!StartsWithAny(img.gameObject.name, KeepColor)) img.color = Color.white;

                        bool keepFill = StartsWithAny(img.gameObject.name, KeepFill);
                        if (!keepFill && sprite.border != Vector4.zero && img.type == Image.Type.Simple)
                            img.type = Image.Type.Sliced;

                        bool isPortrait = key != null && (key.StartsWith("frame_") || key == "board"
                                                          || key == "sold_seal" || key.StartsWith("currency"));
                        img.preserveAspect = isPortrait && !keepFill;
                        touched++;
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    panels++;
                    images += touched;
                    Debug.Log($"[ArtApplier] {file}：挂了 {touched} 张图");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ArtApplier] 完成：{panels} 个面板 / {images} 个 Image" +
                      (missing > 0 ? $"；有 {missing} 个节点找不到对应美术（已在规则里命中但文件缺失）" : ""));
        }

        private static bool StartsWithAny(string name, string[] prefixes)
        {
            foreach (var p in prefixes) if (name.StartsWith(p)) return true;
            return false;
        }

        private static string Resolve(string nodeName, out bool fromScreen)
        {
            fromScreen = false;

            // 透明热区（Hot_）永不挂图：它是盖在底图画好的图标上的命中判定区，
            // 挂了图反而会把美术画好的图标盖住。见 UITool/UIPanelPrefabBuilder.Hotspot。
            if (nodeName.StartsWith("Hot_")) return null;
            if (Exact.TryGetValue(nodeName, out var exact))
            {
                if (exact == "@screen") { fromScreen = true; return null; }
                return exact;
            }
            foreach (var pair in ByPrefix)
                if (nodeName.StartsWith(pair.prefix)) return pair.key;
            return null;
        }
    }
}
