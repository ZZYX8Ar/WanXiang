// ============================================================================
//  WanXiang · 场景结构搭建
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / 场景 / 重建场景结构（Boot / Main / Battle2D）
//
//  三个场景，各挂各的启动脚本：
//    Boot      [UIBootstrap] + [InputBootstrap] + [GameEntry]   —— 打开开始界面
//    Main      [InputBootstrap] + [MainSceneEntry]              —— 主界面 / 或者从战斗回来弹结算
//    Battle2D  [InputBootstrap] + [BattleSceneDriver] + ~Battle2D(BattleStage2D)
//
//  同时：写 Build Settings 顺序（Boot 0 / Main 1 / Battle2D 2），并删除模板遗留的
//  SampleScene（它带着 URP 模板的相机、SmokeTest 与烘焙好的 2D 舞台，与本结构重复）。
//
//  ⚠ 本工具会**覆盖**这三个场景文件；场景里的手工改动请先提交 git。
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.Boot;
using WanXiang.Fusion;
using WanXiang.Modules.Boot;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    public static class SceneSetupTool
    {
        private const string SceneDir = "Assets/Scenes";
        private const string BootPath = SceneDir + "/Boot.unity";
        private const string MainPath = SceneDir + "/Main.unity";
        private const string BattlePath = SceneDir + "/Battle2D.unity";
        private const string SamplePath = SceneDir + "/SampleScene.unity";

        private const string ContentCatalogPath = "Assets/WanXiang/Config/ContentCatalog.asset";
        private const string SpriteCatalogPath = "Assets/WanXiang/Config/SpriteCatalog.asset";
        private const string BattleBgPath = "Assets/ArtRes/BattleBg/bg_spring_far.png";

        [MenuItem("WanXiang/场景/重建场景结构（Boot / Main / Battle2D）", priority = 400)]
        public static void Rebuild()
        {
            // 先落盘当前场景：否则 NewScene 会弹「是否保存」对话框，
            // 自动化调用时对话框无人点，整条流程就卡在那里。
            EditorSceneManager.SaveOpenScenes();

            if (!AssetDatabase.IsValidFolder(SceneDir))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            BuildBoot();
            BuildMain();

            // Battle2D 是 Battle2DBuilderWindow 搭出来的（舞台已烘焙好），这里只补入口，
            // 不重建 —— 重建会把已摆好的舞美推倒。
            PatchBattleScene();

            SetBuildSettings();

            // 收尾停在 Boot：编辑器里 Play 就是从启动场景开始（否则会停在上一步打开的 Battle2D）
            EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Single);

            Debug.Log("[SceneSetup] 场景结构就绪：Boot（启动）/ Main（主城）/ Battle2D（战斗）。" +
                      "已在编辑器里打开 Boot，直接 Play 即可从开始界面走完整条流程。");
        }

        [MenuItem("WanXiang/场景/删除模板遗留 SampleScene", priority = 401)]
        public static void DeleteSampleScene()
        {
            if (!System.IO.File.Exists(System.IO.Path.GetFullPath(SamplePath)))
            {
                Debug.Log("[SceneSetup] SampleScene 已不存在。");
                return;
            }
            AssetDatabase.DeleteAsset(SamplePath);
            SetBuildSettings();
            Debug.Log("[SceneSetup] 已删除 SampleScene（模板遗留：URP 相机 + SmokeTest + 烘焙舞台）。");
        }

        // ================================================================
        //  Boot / Main
        // ================================================================

        private static void BuildBoot()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            MakeCamera();
            var go = new GameObject("[UIBootstrap]");
            go.AddComponent<UIBootstrap>();

            var input = new GameObject("[InputBootstrap]");
            input.AddComponent<InputBootstrap>();

            var entry = new GameObject("[GameEntry]");
            entry.AddComponent<GameEntry>();

            EditorSceneManager.SaveScene(scene, BootPath);
            Debug.Log("[SceneSetup] ✓ " + BootPath);
        }

        private static void BuildMain()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            MakeCamera();
            // 不挂 [InputBootstrap]：它自己 DontDestroyOnLoad，且创建的 [EventSystem] 也跨场景，
            // Boot 里那一份就够。重复挂会被它自杀（并触发一条警告日志）。
            var entry = new GameObject("[MainSceneEntry]");
            entry.AddComponent<MainSceneEntry>();

            EditorSceneManager.SaveScene(scene, MainPath);
            Debug.Log("[SceneSetup] ✓ " + MainPath);
        }

        /// <summary>给战斗场景补入口：舞台驱动 + 输入引导（舞台本身保持原样）。</summary>
        private static void PatchBattleScene()
        {
            if (!System.IO.File.Exists(System.IO.Path.GetFullPath(BattlePath)))
            {
                Debug.LogWarning("[SceneSetup] 没有 Battle2D.unity，先跑 " +
                                 "万相/战斗/2D 战场搭建与回放 里的「搭建战场场景」。");
                return;
            }

            var scene = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Single);

            // 1) 舞台保持激活（它是这个场景的主角）
            var stage = Object.FindObjectOfType<BattleStage2D>();
            if (stage != null && !stage.gameObject.activeSelf) stage.gameObject.SetActive(true);

            // 2) 相机
            if (Object.FindObjectOfType<Camera>() == null) MakeCamera();

            // 3) 战斗驱动（输入服务与 EventSystem 由 Boot 场景带过来，这里不重复挂）

            // 4) 战斗驱动
            var driverGo = GameObject.Find("[BattleSceneDriver]");
            if (driverGo == null) driverGo = new GameObject("[BattleSceneDriver]");
            var driver = driverGo.GetComponent<BattleSceneDriver>();
            if (driver == null) driver = driverGo.AddComponent<BattleSceneDriver>();

            var so = new SerializedObject(driver);
            SetRef(so, "_stage", stage);
            SetRef(so, "_background", AssetDatabase.LoadAssetAtPath<Sprite>(BattleBgPath));
            SetRef(so, "_contentCatalog", AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(ContentCatalogPath));
            SetRef(so, "_sprites", AssetDatabase.LoadAssetAtPath<SpriteCatalog>(SpriteCatalogPath));
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, BattlePath);
            Debug.Log("[SceneSetup] ✓ " + BattlePath + "（已补 [BattleSceneDriver]）");
        }

        // ================================================================
        //  工具
        // ================================================================

        private static void MakeCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.96f, 0.94f, 0.90f);
            cam.orthographic = true;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning("[SceneSetup] 字段不存在：" + field);
                return;
            }
            p.objectReferenceValue = value;
        }

        private static void SetBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (var path in new[] { BootPath, MainPath, BattlePath })
                if (System.IO.File.Exists(System.IO.Path.GetFullPath(path)))
                    list.Add(new EditorBuildSettingsScene(path, true));

            // 模板遗留的 SampleScene 不再入列（文件本身交给「删除模板遗留」菜单）
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[SceneSetup] Build Settings 场景列表：" +
                      string.Join(", ", list.ConvertAll(s => System.IO.Path.GetFileNameWithoutExtension(s.path))));
        }
    }
}
