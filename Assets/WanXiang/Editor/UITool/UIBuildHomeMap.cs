// ============================================================================
//  万相 · UI 工具：主界面「四季旅程」横卷地图（旅行者风，替代终末地圆环）
//  ---------------------------------------------------------------------------
//  在 Panel_Home.prefab 中央补出 HomeMapJourney 所需的整套节点（Ensure 语义：
//  只补缺；旧 Map_Ring 若还在则删掉重建，因为整棵都是工具生成的、无手工美术）：
//
//    Panel_Home
//    └─ Map_Journey  (1240×300 居中, HomeMapJourney; 插到 Img_HeroSprite 之前=垫在立绘下)
//       ├─ Map_Par        视差作用层
//       │  ├─ Img_Far      远山层（大气透视，视差最慢 —— 伪3D 底层）
//       │  ├─ Img_Terrain  主景丘陵+虚线小路（raycastTarget=true 悬浮入口）
//       │  ├─ Map_Nodes    24 个已烘焙节气节点（运行时只染色，不建节点）
//       │  │  └─ Nde_Term_立春 … Nde_Term_大寒
//       │  ├─ Tmp_Season_0..3  四季名标签（春/夏/秋/冬，各自季色，静态）
//       │  ├─ Img_Near     近景层（视差最快 —— 伪3D 前层）
//       │  ├─ Img_Fog       迷雾（从当前进度盖到右缘，左缘渐隐）
//       │  └─ Img_Flag      旅行者小旗（当前位置）
//       └─ Tmp_MapLabel    中央「第X幕·春 / 节气」
//
//  美术：_tools/_gen_homemap3.py（透明通道伪3D三层），替代 v2 的 journey_terrain
//  幂等：Map_Journey 已存在则销毁重建（全子树工具生成）。
// ============================================================================

#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    public static class UIBuildHomeMap
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Home.prefab";
        private const string ArtDir     = "Assets/ArtRes/UI/HomeMap";
        private const string BackupDir  = @"D:\Unity_Project\MYRIAD\_backups";
        private const string ReportPath = "Temp/WanXiangDiag/homemap_report.txt";

        private const float StripW = 1240f;
        private const float StripH = 300f;

        [MenuItem("WanXiang/UI/主界面四季旅程图（横卷）", priority = 112)]
        public static void Build()
        {
            if (!File.Exists(PrefabPath))
            {
                Debug.LogError("[HomeMap] 找不到 " + PrefabPath);
                return;
            }

            EnsureArtImportSettings();
            BackupPrefab();

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                EnsureJourney(root);
                var comp = WireJourney(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[HomeMap] 已保存 prefab → " + PrefabPath);

                bool ok = Verify(comp);
                Debug.Log("[HomeMap] 核验 " + (ok ? "全部通过 ✓（报告见 " + ReportPath + "）" : "有缺项 ✗（见报告）"));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------ 备份（工程外）
        private static void BackupPrefab()
        {
            try
            {
                Directory.CreateDirectory(BackupDir);
                string dst = Path.Combine(BackupDir,
                    "Panel_Home_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".prefab");
                File.Copy(PrefabPath, dst, true);
                Debug.Log("[HomeMap] 已备份 → " + dst);
            }
            catch (Exception e)
            {
                Debug.LogError("[HomeMap] 备份失败，中止：" + e.Message);
                throw;
            }
        }

        // ------------------------------------------------------------------ 美术导入设置
        private static void EnsureArtImportSettings()
        {
            string[] files = { "journey_pano.png", "journey_far.png", "journey_near.png",
                               "journey_path.png", "journey_fog.png", "journey_traveler.png", "map_node.png" };
            foreach (var f in files)
            {
                string p = ArtDir + "/" + f;
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) { Debug.LogWarning("[HomeMap] 缺美术：" + p + "（应先跑 _tools/_gen_homemap2.py）"); continue; }
                if (imp.textureType != TextureImporterType.Sprite || imp.mipmapEnabled)
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.mipmapEnabled = false;
                    imp.SaveAndReimport();
                    Debug.Log("[HomeMap] 导入设置修正 → " + f);
                }
            }
        }

        private static Sprite LoadSprite(string file)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/" + file);
        }

        // ------------------------------------------------------------------ Ensure 节点
        private static void EnsureJourney(GameObject root)
        {
            // 旧圆环（若还在）→ 删（全子树工具生成，无手工美术）
            var oldRing = FindDeep(root.transform, "Map_Ring");
            if (oldRing != null)
            {
                UnityEngine.Object.DestroyImmediate(oldRing.gameObject);
                Debug.Log("[HomeMap] 已删除旧 Map_Ring（圆环方案）");
            }

            var j = FindDeep(root.transform, "Map_Journey");
            if (j != null)
            {
                UnityEngine.Object.DestroyImmediate(j.gameObject);
                Debug.Log("[HomeMap] Map_Journey 已存在 → 销毁重建");
            }

            var hero = FindDeep(root.transform, "Img_HeroSprite");
            Transform parent = hero != null ? hero.transform.parent : root.transform;

            var journey = NewUI("Map_Journey", parent, StripW, StripH);
            journey.AddComponent<HomeMapJourney>();

            var par = NewUI("Map_Par", journey.transform, StripW, StripH);

            // 远山淡墨（AI 出图抠透明；条带上部，视差最慢 —— 伪3D 底层）
            var far = NewUI("Img_Far", par.transform, StripW, 210f);
            var farImg = far.AddComponent<Image>();
            farImg.sprite = LoadSprite("journey_far.png");
            farImg.raycastTarget = false;
            ((RectTransform)far.transform).anchoredPosition = new Vector2(0f, 62f);

            // 主景画卷（AI 四季水墨长卷；raycast 入口）
            var terrain = NewUI("Img_Terrain", par.transform, StripW, StripH);
            var terrainImg = terrain.AddComponent<Image>();
            terrainImg.sprite = LoadSprite("journey_pano.png");
            terrainImg.raycastTarget = true;

            // 白虚线小路（程序化，位置公式与节点一致；盖在画卷上）
            var pathGo = NewUI("Img_Path", par.transform, StripW, StripH);
            var pathImg = pathGo.AddComponent<Image>();
            pathImg.sprite = LoadSprite("journey_path.png");
            pathImg.raycastTarget = false;

            // 24 节气节点（位置公式与 _gen_homemap2.py / HomeMapJourney 一致）
            string[] terms =
            {
                "立春","雨水","惊蛰","春分","清明","谷雨","立夏","小满","芒种","夏至","小暑","大暑",
                "立秋","处暑","白露","秋分","寒露","霜降","立冬","小雪","大雪","冬至","小寒","大寒",
            };
            var nodesRoot = NewUI("Map_Nodes", par.transform, StripW, StripH);
            for (int k = 0; k < 24; k++)
            {
                float xf = (k + 1) / 25f;
                float x = xf * StripW - StripW * 0.5f;
                float y = Mathf.Sin(k * 0.9f) * 24f;
                var node = NewUI("Nde_Term_" + terms[k], nodesRoot.transform, 32f, 32f);
                var ni = node.AddComponent<Image>();
                ni.sprite = LoadSprite("map_node.png");
                ni.raycastTarget = false;
                var nrt = (RectTransform)node.transform;
                nrt.anchoredPosition = new Vector2(x, y);
            }

            // 四季名标签（静态，季色）
            Color[] seasonCols =
            {
                new Color(0.38f, 0.72f, 0.42f), new Color(0.92f, 0.46f, 0.34f),
                new Color(0.90f, 0.68f, 0.28f), new Color(0.48f, 0.70f, 0.95f),
            };
            string[] seasonNames = { "春", "夏", "秋", "冬" };
            for (int s = 0; s < 4; s++)
            {
                int centerNode = 6 * s + 2;                    // 每季中段节点
                float xf = (centerNode + 1) / 25f;
                float x = xf * StripW - StripW * 0.5f;
                var lab = NewUI("Tmp_Season_" + s, par.transform, 140f, 44f);
                var tmp = lab.AddComponent<TextMeshProUGUI>();
                if (UIBuild.Font != null) tmp.font = UIBuild.Font;
                tmp.text = seasonNames[s];
                tmp.fontSize = 30;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = seasonCols[s];
                tmp.raycastTarget = false;
                ((RectTransform)lab.transform).anchoredPosition = new Vector2(x, 108f);
            }

            // 近景墨丘（AI 出图抠透明；条带底部，视差最快 —— 伪3D 前层）
            var near = NewUI("Img_Near", par.transform, StripW, 150f);
            var nearImg = near.AddComponent<Image>();
            nearImg.sprite = LoadSprite("journey_near.png");
            nearImg.raycastTarget = false;
            ((RectTransform)near.transform).anchoredPosition = new Vector2(0f, -75f);

            // 迷雾（运行时控锚点；初始铺满）
            var fog = NewUI("Img_Fog", par.transform, StripW, StripH);
            var fogImg = fog.AddComponent<Image>();
            fogImg.sprite = LoadSprite("journey_fog.png");
            fogImg.raycastTarget = false;
            var frt = (RectTransform)fog.transform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;

            // 小旅行者（AI 水墨小人物；底枢轴 ⇒ 脚踩在小路上）
            var flag = NewUI("Img_Flag", par.transform, 48f, 64f);
            var flagImg = flag.AddComponent<Image>();
            flagImg.sprite = LoadSprite("journey_traveler.png");
            flagImg.raycastTarget = false;
            var frt2 = (RectTransform)flag.transform;
            frt2.pivot = new Vector2(0.5f, 0.05f);

            // 中央文案
            var labelGo = NewUI("Tmp_MapLabel", journey.transform, 340f, 100f);
            var tmpL = labelGo.AddComponent<TextMeshProUGUI>();
            if (UIBuild.Font != null) tmpL.font = UIBuild.Font;
            tmpL.text = "第1幕 · 春\n立春";
            tmpL.fontSize = 30;
            tmpL.alignment = TextAlignmentOptions.Center;
            tmpL.color = seasonCols[0];
            tmpL.raycastTarget = false;
            ((RectTransform)labelGo.transform).anchoredPosition = new Vector2(0f, 118f);

            // 垫到立绘之下
            if (hero != null) journey.transform.SetSiblingIndex(hero.transform.GetSiblingIndex());

            Debug.Log("[HomeMap] + Map_Journey（四季横卷：地形+24节点+四季名+迷雾+小旗，垫在立绘下）");
        }

        private static GameObject NewUI(string name, Transform parent, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        // ------------------------------------------------------------------ 绑定
        private static HomeMapJourney WireJourney(GameObject root)
        {
            var journey = FindDeep(root.transform, "Map_Journey");
            var comp = journey != null ? journey.GetComponent<HomeMapJourney>() : null;
            if (comp == null) { Debug.LogError("[HomeMap] Map_Journey 上无 HomeMapJourney 组件"); return null; }

            var so = new SerializedObject(comp);
            Bind(so, "_par",      FindDeep(journey, "Map_Par"));
            Bind(so, "_terrain",  FindDeep(journey, "Img_Terrain"));
            Bind(so, "_fog",      FindDeep(journey, "Img_Fog"));
            Bind(so, "_nodesRoot", FindDeep(journey, "Map_Nodes"));
            Bind(so, "_marker",   FindDeep(journey, "Img_Flag"));
            Bind(so, "_far",      FindDeep(journey, "Img_Far"));
            Bind(so, "_near",     FindDeep(journey, "Img_Near"));
            Bind(so, "_label",    FindDeep(journey, "Tmp_MapLabel"));
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[HomeMap] HomeMapJourney 字段绑定完成");

            // 顺带接 HomePanel._homeMap
            var panel = root.GetComponent<WanXiang.Modules.UI.HomePanel>();
            if (panel != null)
            {
                var pso = new SerializedObject(panel);
                var pp = pso.FindProperty("_homeMap");
                if (pp != null) { pp.objectReferenceValue = comp; pso.ApplyModifiedPropertiesWithoutUndo(); Debug.Log("[HomeMap] HomePanel._homeMap ← Map_Journey ✓"); }
            }
            return comp;
        }

        // 按字段真实类型取组件：RectTransform / Image / TMP_Text 各取所需
        // （旧版写死 GetComponent<Image>()，会把 _par/_nodesRoot/_label 漏绑）
        private static void Bind(SerializedObject so, string field, Transform node)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError("[HomeMap] 找不到字段 " + field); return; }
            if (node == null) { Debug.LogError("[HomeMap] prefab 缺节点（绑定 " + field + "）"); return; }
            var ft = typeof(HomeMapJourney).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            if (ft == null) { Debug.LogError("[HomeMap] 反射不到字段 " + field); return; }
            var c = node.GetComponent(ft.FieldType);
            if (c == null) { Debug.LogError("[HomeMap] 节点 " + node.name + " 无 " + ft.FieldType.Name + " 组件"); return; }
            p.objectReferenceValue = c;
        }

        // ------------------------------------------------------------------ 反射核验
        private static bool Verify(HomeMapJourney comp)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== HomeMap 核验 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            bool ok = true;
            if (comp == null) { sb.AppendLine("✗ 组件缺失"); WriteReport(sb.ToString()); Debug.Log(sb.ToString()); return false; }

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (var f in typeof(HomeMapJourney).GetFields(flags))
            {
                if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                if (f.FieldType == typeof(HomeMapJourney)) continue;
                var v = f.GetValue(comp);
                if (v == null && f.FieldType.IsClass) { sb.AppendLine("✗ " + f.Name + " = null"); ok = false; }
                else sb.AppendLine("✓ " + f.Name);
            }

            var journey = (comp.transform as RectTransform);
            // 节点数
            var nodesRoot = FindDeep(journey, "Map_Nodes");
            if (nodesRoot != null)
            {
                if (nodesRoot.childCount == 24) sb.AppendLine("✓ Map_Nodes 24 个节气节点");
                else { sb.AppendLine("✗ Map_Nodes 节点数=" + nodesRoot.childCount); ok = false; }
            }
            else { sb.AppendLine("✗ 缺 Map_Nodes"); ok = false; }

            // 垫底位置
            var hero = FindDeep(journey, "Img_HeroSprite") ?? FindHeroParent(journey.root);
            if (hero != null && journey.GetSiblingIndex() > hero.GetSiblingIndex())
            { sb.AppendLine("✗ Map_Journey 应垫在立绘之下"); ok = false; }
            else sb.AppendLine("✓ 层级（垫在立绘下）");

            // Image 层 sprite 就绪（主景必须可受击且有 sprite；其余层不可受击）
            var terrain = FindDeep(journey, "Img_Terrain");
            var ti = terrain != null ? terrain.GetComponent<Image>() : null;
            if (ti == null || !ti.raycastTarget) { sb.AppendLine("✗ Img_Terrain 必须 raycastTarget=true"); ok = false; }
            else if (ti.sprite == null) { sb.AppendLine("✗ Img_Terrain 缺 sprite（主景没了却不报错=白屏元凶）"); ok = false; }
            else sb.AppendLine("✓ Img_Terrain sprite+raycast ✓");

            foreach (var n in new[] { "Img_Far", "Img_Near", "Img_Path" })
            {
                var t = FindDeep(journey, n);
                var img = t != null ? t.GetComponent<Image>() : null;
                if (img == null || img.sprite == null) { sb.AppendLine("✗ " + n + " 缺 sprite"); ok = false; }
                else if (img.raycastTarget) { sb.AppendLine("✗ " + n + " 不应接受 raycast"); ok = false; }
                else sb.AppendLine("✓ " + n + " sprite 就绪（非受击）");
            }

            // 仅 Image 节点查 sprite；Tmp_MapLabel 是 TMP_Text，单独核
            foreach (var n in new[] { "Img_Fog", "Img_Flag" })
            {
                var t = FindDeep(journey, n);
                var img = t != null ? t.GetComponent<Image>() : null;
                if (img == null || img.sprite == null) { sb.AppendLine("✗ " + n + " 缺 sprite"); ok = false; }
                else sb.AppendLine("✓ " + n + " sprite 就绪");
            }
            var lab = FindDeep(journey, "Tmp_MapLabel");
            if (lab == null || lab.GetComponent<TMP_Text>() == null) { sb.AppendLine("✗ Tmp_MapLabel 缺失 TMP_Text"); ok = false; }
            else sb.AppendLine("✓ Tmp_MapLabel (TMP_Text) 就绪");

            sb.AppendLine(ok ? "=== 全部通过 ===" : "=== 存在缺项 ===");
            WriteReport(sb.ToString());
            return ok;
        }

        private static Transform FindHeroParent(Transform root)
        {
            return FindDeep(root, "Img_HeroSprite");
        }

        private static void WriteReport(string text)
        {
            try
            {
                string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, text);
            }
            catch (Exception e) { Debug.LogWarning("[HomeMap] 报告写失败: " + e.Message); }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
#endif
