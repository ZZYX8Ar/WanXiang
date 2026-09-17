// ============================================================================
//  万相 · 2D 战场编辑器预览
//  ---------------------------------------------------------------------------
//  菜单：
//    万相 / 战斗 / 2D 战场预览（编辑器内搭台）   —— 不开 Play 就能看到灵兽站位
//    万相 / 战斗 / 清除 2D 战场预览
//
//  为什么需要它：2D 战场（BattleStage2D）是**运行时程序生成**的 —— 立绘、棋盘、
//  血条、伤害数字全在 Play 时才出现。于是"在编辑器里打开 Battle2D 场景"是空的，
//  打开 Panel_Battle 预制体更是只有 HUD（HUD 里根本没有单位，Root_Stage 在场景
//  模式下是故意清空的），非常容易误判成"战斗画面没做"。
//
//  本工具在编辑器里直接跑一遍装配：组一场默认战斗 → BattleStage2D.Build →
//  10 只灵兽立绘 + 双棋盘 + 血条立刻出现在 Scene 视图里。美术调站位、策划看
//  阵型都靠它。
//
//  ⚠ 预览物体标了 HideFlags.DontSave，**不会被保存进场景**；Scene 视图不需要
//    战场相机，所以建完就把 Cam2D 删掉（否则它会跟着场景走、还会抢 Game 视图）。
// ============================================================================

using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
using WanXiang.Fusion;
using WanXiang.Modules.UI;
using WanXiang.Run;

namespace WanXiang.EditorTools
{
    public static class Battle2DPreviewTool
    {
        private const string PreviewRootName = "~Battle2D预览";
        private const string CatalogPath = "Assets/WanXiang/Config/ContentCatalog.asset";
        private const string SpriteCatalogPath = "Assets/WanXiang/Config/SpriteCatalog.asset";
        private const string BgPath = "Assets/ArtRes/BattleBg/bg_spring_mid.png";

        [MenuItem("万相/战斗/2D 战场预览（编辑器内搭台）", priority = 120)]
        public static void Preview()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[2D预览] Play 模式下不用它 —— 直接看 Game 视图，那里就是真实战场。");
                return;
            }

            ClearPreview();

            var root = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            var stage = root.AddComponent<BattleStage2D>();

            var req = BuildRequest();
            var playback = new BattlePlayback(req);
            var bg = AssetDatabase.LoadAssetAtPath<Sprite>(BgPath);
            var sprites = AssetDatabase.LoadAssetAtPath<SpriteCatalog>(SpriteCatalogPath);

            stage.Build(playback.State, bg, sprites);

            // 编辑器里不需要战场相机：留着会进场景、还会抢 Game 视图
            var cam = root.transform.Find("Cam2D");
            if (cam != null) Object.DestroyImmediate(cam.gameObject);

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

            int units = root.transform.Find("BoardP") != null
                      ? CountUnits(root.transform) : 0;
            Debug.Log($"[2D预览] 已搭台：我方 {playback.State.UnitsOf(TeamSide.Player).Count} 只、" +
                      $"敌方 {playback.State.UnitsOf(TeamSide.Enemy).Count} 只，立绘 {units} 个渲染体。" +
                      $"（预览物体不会存进场景，看完用「万相/战斗/清除 2D 战场预览」删掉）");
        }

        [MenuItem("万相/战斗/清除 2D 战场预览", priority = 121)]
        public static void ClearPreview()
        {
            var existing = GameObject.Find(PreviewRootName);
            while (existing != null)
            {
                Object.DestroyImmediate(existing);
                existing = GameObject.Find(PreviewRootName);
            }
        }

        private static int CountUnits(Transform root)
        {
            int n = 0;
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sprite != null) n++;
            return n;
        }

        /// <summary>有存档就照存档组队（能顺便验算真实进度），没有就用默认队伍。</summary>
        private static BattleRequest BuildRequest()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(CatalogPath);
            BattleRequest req = null;
            var run = RunSave.Current;
            if (run != null && BattleRequestFactory.TryBuildFromRun(catalog, run, "无天时", out req))
                return req;
            BattleRequestFactory.TryBuild(catalog, "预览 · 遭遇战", "无天时", 20260917UL, out req);
            return req;
        }
    }
}
