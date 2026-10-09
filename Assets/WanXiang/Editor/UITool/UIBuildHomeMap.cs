// ============================================================================
//  UI 工具：主界面中央地图（四季节气环 · 终末地风格）
//  ---------------------------------------------------------------------------
//  在 Panel_Home.prefab 中央补出 HomeMapRing 所需的整套节点（Ensure 语义：
//  只补缺，绝不重建/移动已有节点），并接好全部 SerializedField 绑定。
//
//  结构（新增部分，Map_Ring 垫在 Img_HeroSprite 之下 = 立绘在地图上层）：
//    Panel_Home
//    └─ Map_Ring   (560×560 居中, HomeMapRing; 悬浮事件从子节点冒泡上来)
//       ├─ Map_Tilt        悬浮晃动作用层（旋转/平移/放大都动它）
//       │  ├─ Img_MapDisc   等高线圆盘（raycastTarget=true，悬浮事件入口）
//       │  ├─ Img_Sea_Spring / Summer / Autumn / Winter（扇区 Radial360）
//       │  ├─ Map_Nodes     24 节点容器
//       │  │  └─ Nde_Template  节点模板（默认隐藏，运行时克隆 24 个）
//       │  └─ Img_MapFrame  外环 + 刻度
//       ├─ Img_Marker      当前节气菱形标记
//       └─ Tmp_MapLabel    中央「第X幕·春 / 节气」
//
//  幂等：Map_Ring 已存在时只重做绑定与美术导入设置，不动布局。
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

        [MenuItem("WanXiang/UI/主界面中央地图（四季环）", priority = 112)]
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
                bool created = EnsureRing(root);
                WireRing(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[HomeMap] " + (created ? "已新建 Map_Ring 并" : "Map_Ring 已存在，仅")
                          + "保存 prefab → " + PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            bool ok = Verify();
            Debug.Log("[HomeMap] 核验 " + (ok ? "全部通过 ✓（报告见 " + ReportPath + "）" : "有缺项 ✗（见报告）"));
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
            string[] files = { "map_disc.png", "map_frame.png", "map_sector_glow.png", "map_node.png", "map_marker.png" };
            foreach (var f in files)
            {
                string p = ArtDir + "/" + f;
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null)
                {
                    Debug.LogWarning("[HomeMap] 缺美术：" + p + "（应先跑 _tools/_gen_homemap.py）");
                    continue;
                }
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
        private static bool EnsureRing(GameObject root)
        {
            if (FindDeep(root.transform, "Map_Ring") != null) return false;

            // Map_Ring 垫在立绘之下：插到 Img_HeroSprite 之前
            var hero = FindDeep(root.transform, "Img_HeroSprite");
            Transform parent = hero != null ? hero.transform.parent : root.transform;

            var ring = NewUI("Map_Ring", parent, 560f, 560f);
            ring.AddComponent<HomeMapRing>();

            var tilt = NewUI("Map_Tilt", ring.transform, 560f, 560f);

            // 圆盘：唯一的 raycastTarget（悬浮事件入口）
            var disc = NewUI("Img_MapDisc", tilt.transform, 560f, 560f);
            var discImg = disc.AddComponent<Image>();
            discImg.sprite = LoadSprite("map_disc.png");
            discImg.raycastTarget = true;

            // 四季扇区（Radial360 填充，运行时由 HomeMapRing 控 fillAmount/color）
            string[] seaNames = { "Img_Sea_Spring", "Img_Sea_Summer", "Img_Sea_Autumn", "Img_Sea_Winter" };
            foreach (var n in seaNames)
            {
                var go = NewUI(n, tilt.transform, 560f, 560f);
                var img = go.AddComponent<Image>();
                img.sprite = LoadSprite("map_sector_glow.png");
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Radial360;
                img.fillClockwise = true;
                img.raycastTarget = false;
            }

            // 24 节点容器 + 模板
            var nodesRoot = NewUI("Map_Nodes", tilt.transform, 560f, 560f);
            var nodeT = NewUI("Nde_Template", nodesRoot.transform, 26f, 26f);
            var nodeImg = nodeT.AddComponent<Image>();
            nodeImg.sprite = LoadSprite("map_node.png");
            nodeImg.raycastTarget = false;
            nodeT.SetActive(false);

            // 外环刻度
            var frame = NewUI("Img_MapFrame", tilt.transform, 560f, 560f);
            var frameImg = frame.AddComponent<Image>();
            frameImg.sprite = LoadSprite("map_frame.png");
            frameImg.raycastTarget = false;

            // 当前节气标记
            var marker = NewUI("Img_Marker", ring.transform, 34f, 34f);
            var markerImg = marker.AddComponent<Image>();
            markerImg.sprite = LoadSprite("map_marker.png");
            markerImg.raycastTarget = false;

            // 中央文案
            var labelGo = NewUI("Tmp_MapLabel", ring.transform, 280f, 110f);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            if (UIBuild.Font != null) tmp.font = UIBuild.Font;
            tmp.text = "第1幕 · 春\n立春";
            tmp.fontSize = 30;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.45f, 0.88f, 0.55f, 1f);
            tmp.raycastTarget = false;

            // 垫到立绘下面
            if (hero != null) ring.transform.SetSiblingIndex(hero.transform.GetSiblingIndex());

            Debug.Log("[HomeMap] + Map_Ring（四季环整套，垫在 Img_HeroSprite 之下）");
            return true;
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
        private static void WireRing(GameObject root)
        {
            var ring = FindDeep(root.transform, "Map_Ring");
            var comp = ring != null ? ring.GetComponent<HomeMapRing>() : null;
            if (comp == null) { Debug.LogError("[HomeMap] Map_Ring 上没有 HomeMapRing 组件"); return; }

            var so = new SerializedObject(comp);
            Bind(so, "_tilt",   FindDeep(ring, "Map_Tilt"),    typeof(RectTransform));
            Bind(so, "_disc",   FindDeep(ring, "Img_MapDisc"), typeof(Image));
            Bind(so, "_frame",  FindDeep(ring, "Img_MapFrame"), typeof(Image));
            Bind(so, "_nodesRoot", FindDeep(ring, "Map_Nodes"), typeof(RectTransform));
            Bind(so, "_nodeTemplate", FindDeep(ring, "Nde_Template"), typeof(Image));
            Bind(so, "_marker", FindDeep(ring, "Img_Marker"),  typeof(Image));
            Bind(so, "_label",  FindDeep(ring, "Tmp_MapLabel"), typeof(TMP_Text));

            var sectors = so.FindProperty("_sectors");
            if (sectors != null && sectors.isArray)
            {
                string[] names = { "Img_Sea_Spring", "Img_Sea_Summer", "Img_Sea_Autumn", "Img_Sea_Winter" };
                sectors.arraySize = 4;
                for (int i = 0; i < 4; i++)
                {
                    var t = FindDeep(ring, names[i]);
                    sectors.GetArrayElementAtIndex(i).objectReferenceValue =
                        t != null ? t.GetComponent<Image>() : null;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[HomeMap] HomeMapRing 字段绑定完成");

            // 顺带接 HomePanel._homeMap（主城每次打开 → Refresh）
            var panel = root.GetComponent<WanXiang.Modules.UI.HomePanel>();
            if (panel != null)
            {
                var pso = new SerializedObject(panel);
                var pp = pso.FindProperty("_homeMap");
                if (pp != null)
                {
                    pp.objectReferenceValue = comp;
                    pso.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[HomeMap] HomePanel._homeMap ← Map_Ring ✓");
                }
            }
        }

        private static void Bind(SerializedObject so, string field, Transform node, Type type)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError("[HomeMap] HomeMapRing 找不到字段 " + field); return; }
            if (node == null) { Debug.LogError("[HomeMap] prefab 里找不到节点（绑定 " + field + " 失败）"); return; }
            var c = node.GetComponent(type);
            if (c == null) { Debug.LogError("[HomeMap] 节点 " + node.name + " 上没有 " + type.Name); return; }
            p.objectReferenceValue = c;
        }

        // ------------------------------------------------------------------ 反射核验
        private static bool Verify()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== HomeMap 核验 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            bool ok = true;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("[HomeMap] prefab 载入失败"); return false; }

            var ring = FindDeep(prefab.transform, "Map_Ring");
            if (ring == null) { Debug.LogError("[HomeMap] 核验失败：Map_Ring 不存在"); return false; }
            var comp = ring.GetComponent<HomeMapRing>();
            if (comp == null) { Debug.LogError("[HomeMap] 核验失败：HomeMapRing 组件缺失"); return false; }

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (var f in typeof(HomeMapRing).GetFields(flags))
            {
                if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                if (f.FieldType == typeof(HomeMapRing)) continue;   // 自引用跳过

                object v = f.GetValue(comp);
                var arr = v as Array;
                if (arr != null)
                {
                    int bad = 0;
                    foreach (var e in arr) if (e == null) bad++;
                    sb.AppendLine((bad == 0 ? "✓ " : "✗ ") + f.Name + " len=" + arr.Length + (bad > 0 ? " 缺" + bad : ""));
                    if (bad > 0) ok = false;
                }
                else if (v == null)
                {
                    // 表现参数（float 等）有默认值不为 null；对象字段为 null 才算缺
                    if (f.FieldType.IsClass) { sb.AppendLine("✗ " + f.Name + " = null"); ok = false; }
                    else sb.AppendLine("· " + f.Name + " = " + v);
                }
                else sb.AppendLine("✓ " + f.Name);
            }

            // 垫底位置：Map_Ring 必须在 Img_HeroSprite 之前
            var hero = FindDeep(prefab.transform, "Img_HeroSprite");
            if (hero != null && ring.GetSiblingIndex() > hero.GetSiblingIndex())
            {
                sb.AppendLine("✗ Map_Ring 应垫在立绘之下（sibling " + ring.GetSiblingIndex()
                              + " > hero " + hero.GetSiblingIndex() + "）");
                ok = false;
            }
            else sb.AppendLine("✓ Map_Ring 层级（垫在立绘之下）");

            // 关键行为：圆盘必须可接收射线（悬浮事件入口）
            var disc = FindDeep(ring, "Img_MapDisc");
            var discImg = disc != null ? disc.GetComponent<Image>() : null;
            if (discImg == null || !discImg.raycastTarget)
            {
                sb.AppendLine("✗ Img_MapDisc 必须 raycastTarget=true（否则悬浮无事件）");
                ok = false;
            }
            else sb.AppendLine("✓ Img_MapDisc.raycastTarget=true");

            var tpl = FindDeep(ring, "Nde_Template");
            if (tpl == null || tpl.gameObject.activeSelf)
            {
                sb.AppendLine("✗ Nde_Template 必须存在且默认隐藏");
                ok = false;
            }
            else sb.AppendLine("✓ Nde_Template 隐藏");

            sb.AppendLine(ok ? "=== 全部通过 ===" : "=== 存在缺项 ===");
            WriteReport(sb.ToString());
            Debug.Log("[HomeMap] 核验报告 → " + ReportPath + "\n" + sb.ToString());
            return ok;
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
