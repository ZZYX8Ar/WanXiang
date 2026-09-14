// ============================================================================
//  万相 · 资源 · YooAsset 收集器配置工具
//  ---------------------------------------------------------------------------
//  把「YooAsset 收集器怎么配」这件事写成代码，而不是靠人去面板点。
//  理由与 InputAssetGenerator / ExcelConfigExporter 完全一致：
//  配置是人会忘、机器不会忘的东西。写进代码后：
//    · 换台机器 clone 下来，跑一次菜单就是一致的配置；
//    · 配置改了能在 diff 里看见 "把 Art 目录的打包规则从 A 改成 B"，
//      而不是埋在 AssetBundleCollectorSetting.asset 的 YAML 里没人发现；
//    · CI 可以在出包前先跑一次校验，配置漂了就报错。
//
//  ⚠ 本工具放在**独立程序集** WanXiang.Editor.YooAsset 里，
//    不并进 WanXiang.Editor。因为 WanXiang.Editor 里装着诊断通道
//    （EditorDiagnosticsBridge）—— 那条通道的存在意义就是
//    "别的都坏了的时候还能用它排查"，它一旦引用 YooAsset，
//    YooAsset 出问题就会连带把它一起打死。诊断通道调本工具走反射。
//
//  ⚠ 关于「地址用资源路径还是用可寻址名」——本工具的取舍：
//    采用 **EnableAddressable = false**，也就是 location 就是资源路径。
//    代价：面板/配置表的 key 是 "Assets/WanXiangRes/UI/TestPanel" 这种长串，
//          加载时要拼前缀（见 YooAssetPanelLoader 的 prefix/suffix 参数）。
//    收益：不可能出现地址冲突。
//          EnableAddressable = true 时地址由 AddressByFileName 从文件名生成，
//          两个不同目录下的同名文件（比如 UI/Icon.prefab 和 Art/Icon.prefab）
//          会生成同一个地址，YooAsset 直接报配置错误。
//          这种错在资源量上来之后才出现，那时候改地址规则 = 全项目搜改加载路径。
//    这条路是"现在麻烦一点、以后不用回头"。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;
using WanXiang.Framework.ResourceSystem;

namespace WanXiang.Editor.YooTool
{
    /// <summary>
    /// YooAsset 收集器配置的建立与检查。
    /// </summary>
    public static class YooAssetSetupTool
    {
        /// <summary>资源包名。必须与 <see cref="ResourceInitOptions.DefaultPackageName"/> 一致。</summary>
        public const string PackageName = ResourceInitOptions.DefaultPackageName;

        /// <summary>工程内所有可加载资源的根目录。</summary>
        public const string ResourceRoot = "Assets/WanXiangRes";

        /// <summary>组名。以后要分「首包必带 / 按需下载」时按组拆分。</summary>
        public const string GroupName = "Base";

        /// <summary>UI 面板 Prefab 目录。</summary>
        public const string UiFolder = ResourceRoot + "/UI";

        /// <summary>美术资源目录。</summary>
        public const string ArtFolder = ResourceRoot + "/Art";

        /// <summary>配置表 / 文本 / 二进制目录（走原生文件打包规则）。</summary>
        public const string ConfigFolder = ResourceRoot + "/Config";

        /// <summary>
        /// 热更产物目录：热更 DLL + AOT 元数据 DLL + 清单。
        /// </summary>
        /// <remarks>
        /// ⚠ 里面的文件一律带 `.bytes` 后缀。原因见
        ///   <see cref="WanXiang.Framework.HotUpdate.HotUpdateLocations"/> 的说明：
        ///   `Assets/` 下裸的 `.dll` 会被 Unity 当托管插件导入，
        ///   于是同一个程序集被编译两次，报 `CS0433 ... exists in both`。
        /// </remarks>
        public const string HotUpdateFolder = ResourceRoot + "/HotUpdate";

        /// <summary>热更产物的 AOT 元数据子目录。</summary>
        public const string HotUpdateAotFolder = HotUpdateFolder + "/AOT";

        /// <summary>
        /// 热更产物的组名。
        /// </summary>
        /// <remarks>
        /// ⚠ 刻意**单独一个组**，不并进 <see cref="GroupName"/>。
        ///   组 → 打包粒度的映射意味着独立组会产出独立 bundle，
        ///   于是热更时客户端**只需要下载那一个 bundle**，
        ///   而不是连美术资源一起重下。这是热更方案能不能用的分水岭。
        /// </remarks>
        public const string HotUpdateGroupName = "HotUpdate";

        // ==================================================================
        //  菜单入口
        // ==================================================================

        [MenuItem("万相/资源/初始化 YooAsset 收集器配置", false, 100)]
        public static void MenuSetup()
        {
            foreach (string line in Setup())
            {
                Debug.Log("[资源工具] " + line);
            }
        }

        [MenuItem("万相/资源/打印收集器状态", false, 101)]
        public static void MenuStatus()
        {
            foreach (string line in Status())
            {
                Debug.Log("[资源工具] " + line);
            }
        }

        [MenuItem("万相/资源/为资源系统创建测试资源", false, 102)]
        public static void MenuCreateTestAssets()
        {
            foreach (string line in CreateTestAssets())
            {
                Debug.Log("[资源工具] " + line);
            }
        }

        // ==================================================================
        //  诊断通道入口（反射调用，见文件头说明）
        // ==================================================================

        /// <summary>
        /// 诊断通道的统一入口。
        /// </summary>
        /// <param name="command">yoo.setup / yoo.status / yoo.testassets</param>
        /// <returns>逐行报告。由诊断通道原样写进 report.json。</returns>
        /// <remarks>
        /// ⚠ 返回 string[] 而不是一个大字符串：诊断通道把每一项作为
        ///   report.results 的一行，多行字符串会挤成一行，很难读。
        /// </remarks>
        public static string[] Run(string command)
        {
            switch (command)
            {
                case "yoo.setup":
                    return Setup();
                case "yoo.status":
                    return Status();
                case "yoo.testassets":
                    return CreateTestAssets();
                default:
                    return new[] { $"❌ YooAssetSetupTool 不认识命令：{command}" };
            }
        }

        // ==================================================================
        //  建立配置
        // ==================================================================

        /// <summary>
        /// 建目录 → 建测试资源 → 配置收集器 → 保存。
        /// </summary>
        public static string[] Setup()
        {
            var report = new List<string>();

            EnsureFolders(report);
            report.AddRange(CreateTestAssets());
            EnsureCollectorSetting(report);

            // ⚠⚠ 这里必须走 YooAsset 自己的 SaveFile()，
            //   **不能**只调 AssetDatabase.SaveAssets()。这一条是踩出来的：
            //
            //   CreatePackage / CreateGroup / CreateCollector 这几个 API
            //   内部只做了一件事 —— 把 AssetBundleCollectorSettingData.IsDirty
            //   置为 true。它们**从不碰 ScriptableObject 的脏标记**。
            //   而 AssetDatabase.SaveAssets() 只保存「已被标记为脏」的对象，
            //   于是磁盘上的 AssetBundleCollectorSetting.asset 永远停在
            //   建库那一刻的空状态（Packages: []），收集器一个都没落盘。
            //
            //   最坑的地方是它**看起来完全正常**：
            //   AssetBundleCollectorSettingData.Setting 返回的就是我们刚改的那个
            //   内存对象，所以任何自检、任何打印都会说"配置没问题"。
            //   只有等 YooAsset 真去磁盘取配置时才炸，报的还是句很短的
            //   "Not found package : xxx"（被反射包成 TargetInvocationException），
            //   而配置窗口里明明看得见那个包。
            //
            //   中间还隔着一次域重载：改完当场不域重载，内存还算数；
            //   一旦 Unity 重新编译并重载域，静态 _setting 归位，
            //   从空文件里读回来 —— 前面那次"成功"就像没发生过。
            //
            //   SaveFile() 里做的就是 EditorUtility.SetDirty(Setting)
            //   + AssetDatabase.SaveAssets()，还会打一行
            //   "AssetBundleCollectorSetting.asset is saved!"。
            //   控制台里没这行，就说明配置根本没写下去。
            AssetBundleCollectorSettingData.SaveFile();

            AssetDatabase.Refresh();

            // 复核：直接读磁盘文件，确认真的落盘了（而不是再看一遍内存）。
            report.AddRange(VerifyPersisted());
            return report.ToArray();
        }

        /// <summary>
        /// 复核收集器配置是否真的写到了磁盘上。
        /// </summary>
        /// <remarks>
        /// ⚠ 所有「写配置」的工具都该有这一步：写内存成功、落盘失败，
        ///   是最难查的一类失效 —— 工具自己报成功，配置界面也能看到数据，
        ///   只有真正读取的时候才发现是空的。
        ///
        /// ⚠ 复核**必须直接读文件**，不能复用内存对象：
        ///   AssetDatabase.LoadAssetAtPath 命中缓存时会把同一个实例还给你，
        ///   拿它复查等于对着内存照镜子，什么也验不出来。
        ///   这里读的是 Assets/AssetBundleCollectorSetting.asset 的 YAML 原文。
        /// </remarks>
        private static IEnumerable<string> VerifyPersisted()
        {
            var lines = new List<string>();

            // Application.dataPath 指向 <工程>/Assets，配置文件就在它根下。
            string fullPath = Path.Combine(
                Application.dataPath,
                nameof(AssetBundleCollectorSetting) + ".asset");

            if (!File.Exists(fullPath))
            {
                lines.Add($"❌ 落盘复核失败：磁盘上没有 {fullPath}");
                return lines;
            }

            string text = File.ReadAllText(fullPath);
            if (text.Contains("PackageName: " + PackageName))
            {
                lines.Add($"✅ 落盘复核通过（直接读文件）：{SettingAssetPath()} 里已有包裹「{PackageName}」");
            }
            else
            {
                lines.Add($"❌ 落盘复核失败：{SettingAssetPath()} 的磁盘内容里没有 " +
                          $"'PackageName: {PackageName}'。配置只写进了内存。" +
                          "多半是漏调 AssetBundleCollectorSettingData.SaveFile()。");
            }

            return lines;
        }

        /// <summary>
        /// 收集器配置文件的路径。
        /// </summary>
        /// <remarks>
        /// ⚠ YooAsset 把它固定放在 Assets/ 根目录下（SettingLoader 里是硬编码的
        ///   $"Assets/{settingType.Name}.asset"），**不能**挪到别的目录。
        ///   顺便：这个路径也是 .gitignore 要留意的对象 —— 它是配置，
        ///   必须入库，不要被通配规则误伤。
        /// </remarks>
        public static string SettingAssetPath()
        {
            return $"Assets/{nameof(AssetBundleCollectorSetting)}.asset";
        }

        private static void EnsureFolders(List<string> report)
        {
            EnsureFolder(ResourceRoot, report);
            EnsureFolder(UiFolder, report);
            EnsureFolder(ArtFolder, report);
            EnsureFolder(ConfigFolder, report);

            // 热更产物目录。由「万相/热更/发布热更产物」往里填 DLL，
            // 这里只保证目录结构存在，没内容时**不建收集器**（见 EnsureHotUpdateGroup）。
            EnsureFolder(HotUpdateFolder, report);
            EnsureFolder(HotUpdateAotFolder, report);
        }

        /// <summary>
        /// 建一个 Unity 认识的目录（必要时递归建父级）。
        /// </summary>
        /// <remarks>
        /// ⚠⚠ 必须用 <c>AssetDatabase.CreateFolder</c>，**不要**用
        ///    <c>Directory.CreateDirectory</c> 在 Assets 下直接建目录。
        ///
        ///    实测踩过：热更发布工具原本自己写了一句 Directory.CreateDirectory 建
        ///    `Assets/WanXiangRes/HotUpdate`（绕过 Unity 的资产数据库），随后本类又用
        ///    AssetDatabase.CreateFolder 建同名目录 —— Unity 认为"资产库里还没有那个名字"，
        ///    于是把第二次建的目录**改名**成 `HotUpdate 1`。
        ///    结果是磁盘上凭空多了一个空目录 + 一个孤立 .meta，
        ///    而且它不在任何收集器里，沉默地留在版本库里。
        ///
        ///    所以建目录这件事只留这一个实现，别人要建目录就走这里 ——
        ///    两份实现正是这个 bug 的成因。
        /// </remarks>
        public static void EnsureFolder(string folder, List<string> report)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                report.Add($"❌ 目录路径非法：{folder}");
                return;
            }

            // 父级可能也不存在（例如 AOT 子目录的父级是刚建的 HotUpdate）。
            EnsureFolder(parent, report);

            AssetDatabase.CreateFolder(parent, leaf);
            report.Add($"＋ 建立目录 {folder}");
        }

        /// <summary>
        /// 建测试资源。
        /// </summary>
        /// <remarks>
        /// ⚠ 这些是**给资源链路做体检用的样本**，不是游戏内容。
        ///   三个样本各自覆盖一条不同的加载路径：
        ///     · Prefab  → InstantiateAsync（面板走的就是这条）
        ///     · Texture → LoadAssetAsync&lt;Texture2D&gt;（普通资源）
        ///     · JSON    → LoadTextAsync（原生文件 / 配置表走这条）
        ///   链路验通之后可以删掉，但建议留着 —— 以后改收集器配置时
        ///   它们是第一道回归检验。
        /// </remarks>
        public static string[] CreateTestAssets()
        {
            var report = new List<string>();

            // ---- ① Prefab ----
            string prefabPath = UiFolder + "/TestPanel.prefab";
            if (File.Exists(prefabPath))
            {
                report.Add($"＝ 已存在 {prefabPath}");
            }
            else
            {
                var go = new GameObject("TestPanel");
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                    report.Add($"＋ 建立 {prefabPath}");
                }
                finally
                {
                    // ⚠ 必须立刻销毁这个临时物体，否则它会留在当前**打开的场景**里，
                    //   把场景弄脏（Unity 会弹"是否保存"）。这是编辑器脚本最常见的副作用。
                    Object.DestroyImmediate(go);
                }
            }

            // ---- ② Texture（真实 png，不是 .asset） ----
            string pngPath = ArtFolder + "/TestTexture.png";
            if (File.Exists(pngPath))
            {
                report.Add($"＝ 已存在 {pngPath}");
            }
            else
            {
                // 32×32 纯色。尺寸给足是因为 1×1 的贴图有时会被导入设置
                // 压成奇怪的东西，反而不便于确认"确实是这张图"。
                var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                var pixels = new Color32[32 * 32];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(220, 60, 60, 255);
                }
                texture.SetPixels32(pixels);
                texture.Apply();

                byte[] png = texture.EncodeToPNG();
                Object.DestroyImmediate(texture);

                File.WriteAllBytes(pngPath, png);
                AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
                report.Add($"＋ 建立 {pngPath}（32×32 png，{png.Length} 字节）");
            }

            // ---- ③ JSON 文本 ----
            string jsonPath = ConfigFolder + "/test_config.json";
            if (File.Exists(jsonPath))
            {
                report.Add($"＝ 已存在 {jsonPath}");
            }
            else
            {
                // 写纯 ASCII，避免不同编辑器/工具读出乱码。
                const string json =
                    "{\n" +
                    "  \"name\": \"wanxiang\",\n" +
                    "  \"purpose\": \"resource pipeline smoke test\",\n" +
                    "  \"magic\": \"WANXIANG_RESOURCE_OK\"\n" +
                    "}\n";
                File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(jsonPath, ImportAssetOptions.ForceUpdate);
                report.Add($"＋ 建立 {jsonPath}");
            }

            AssetDatabase.Refresh();
            return report.ToArray();
        }

        // ==================================================================
        //  收集器配置
        // ==================================================================

        private static void EnsureCollectorSetting(List<string> report)
        {
            // ⚠ 首次访问 Setting 时，如果工程里没有配置文件，YooAsset 会自动
            //   在 Assets/ 下建一个空的。这是它的设计，不用我们处理。
            AssetBundleCollectorSetting setting = AssetBundleCollectorSettingData.Setting;
            if (setting == null)
            {
                report.Add("❌ 无法取得 AssetBundleCollectorSetting（YooAsset 未正常加载？）");
                return;
            }

            // ---- 包裹 ----
            AssetBundleCollectorPackage package = FindPackage(setting, PackageName);
            if (package == null)
            {
                package = AssetBundleCollectorSettingData.CreatePackage(PackageName);
                report.Add($"＋ 新建包裹「{PackageName}」");
            }
            else
            {
                report.Add($"＝ 包裹「{PackageName}」已存在");
            }

            // 地址策略见文件头说明：用资源路径，躲开同名文件的地址冲突。
            package.EnableAddressable = false;

            // 地址省略扩展名：location 变成 "Assets/.../TestPanel" 而不是
            // ".../TestPanel.prefab"。这样加载代码不用记住每种资源的扩展名。
            package.SupportExtensionless = true;

            // ⚠ 保持 false。打开它会把所有 location 转小写，
            //   于是加载时必须处处小写 —— 一旦有人写了 "Panel_Login"，
            //   就会静默地找不到资源（因为清单里存的是小写）。
            //   Android 上大小写敏感，这个开关看着诱人，实际是给未来埋雷。
            package.LocationToLower = false;

            package.IncludeAssetGUID = false;
            package.AutoCollectShaders = true;
            report.Add($"  包裹设置：EnableAddressable={package.EnableAddressable}（false = 地址用资源路径），" +
                       $"SupportExtensionless={package.SupportExtensionless}，" +
                       $"LocationToLower={package.LocationToLower}");

            // ---- 组 ----
            AssetBundleCollectorGroup group = FindGroup(package, GroupName);
            if (group == null)
            {
                group = AssetBundleCollectorSettingData.CreateGroup(package, GroupName);
                report.Add($"＋ 新建组「{GroupName}」");
            }

            // ---- 收集器 ----
            // Art / UI：普通资源，按目录打包。
            // Config：原生文件（PackRawFile）—— 只有这条规则产出的
            //         RawBundle 才能用 LoadRawFileAsync / LoadTextAsync 读。
            //         ⚠ 注意「原生文件」是**打包规则**，不是收集器类型。
            //           YooAsset 2.3 的 ECollectorType 里没有 RawFileCollector
            //           （那是 1.x 的说法），照那个去找会白找半天。
            EnsureCollector(group, ArtFolder, nameof(PackDirectory), report);
            EnsureCollector(group, UiFolder, nameof(PackDirectory), report);
            EnsureCollector(group, ConfigFolder, nameof(PackRawFile), report);

            EnsureHotUpdateGroup(package, report);

            AssetBundleCollectorSettingData.ModifyPackage(package);
        }

        /// <summary>
        /// 配好热更产物的组与收集器。
        /// </summary>
        /// <remarks>
        /// ⚠ 只在目录里**确实有文件**时才建。
        ///
        ///   原因是空目录 + <c>CollectAll</c> 过滤器会让 YooAsset 收集到 0 个资源，
        ///   然后模拟构建时那个组是空的 —— 报出来的东西很像配置错误，
        ///   但真实原因只是"还没发布过热更产物"。
        ///   与其让使用者去猜这条空组是什么意思，不如干脆不建：
        ///   没发布 ⇒ 没有这个组 ⇒ 报告里说"热更产物目录是空的，先跑发布"。
        ///
        ///   发布工具复制完文件后会再调一次本方法把组补上，所以顺序不会错。
        /// </remarks>
        private static void EnsureHotUpdateGroup(AssetBundleCollectorPackage package, List<string> report)
        {
            AssetBundleCollectorGroup existing = FindGroup(package, HotUpdateGroupName);

            bool hasContent = HasAnyFile(HotUpdateFolder);
            if (!hasContent)
            {
                // 没有产物 ⇒ 不该有这个组。
                // ⚠ 不只是"不新建"，还要**删掉已有的** —— 因为收集器配置是持久化数据，
                //   而热更产物目录**不入库**（生成的二进制，见 .gitignore）。
                //   于是别人 clone 下来时：配置里有这个组、磁盘上没有那个目录。
                //   一个指向不存在目录的收集器，会让 YooAsset 的收集阶段行为不可预期
                //   （最坏情况又变成"资源初始化失败 ⇒ 看起来像热更坏了"）。
                //   让它俩**始终一致**：有产物才有组，没产物就没组。
                if (existing != null)
                {
                    AssetBundleCollectorSettingData.RemoveGroup(package, existing);
                    report.Add($"✂ 热更产物目录是空的，删除组「{HotUpdateGroupName}」"
                               + "（避免配置里有组、磁盘上没目录的不一致状态）");
                }
                else
                {
                    report.Add($"○ 热更产物目录还是空的（{HotUpdateFolder}），本次不建热更组。"
                               + "跑「万相/热更/发布热更产物」之后会自带建组。");
                }

                return;
            }

            AssetBundleCollectorGroup group = existing;
            if (group == null)
            {
                group = AssetBundleCollectorSettingData.CreateGroup(package, HotUpdateGroupName);
                report.Add($"＋ 新建组「{HotUpdateGroupName}」");
            }
            else
            {
                report.Add($"＝ 组「{HotUpdateGroupName}」已存在");
            }

            // ⚠⚠ 只建**一个**收集器，指向 HotUpdate 根目录。
            //    绝不要给 AOT 子目录再建一个 —— 收集器的 CollectAll 是**递归**的，
            //    子目录已经被父收集器收进去了；再加一个子收集器会让同一批文件被收两遍，
            //    YooAsset 直接抛：
            //        The collecting asset file is existed : <路径> in group : HotUpdate
            //    而这个异常会把**整条资源初始化**打死（模拟构建失败 ⇒ 资源不就绪 ⇒
            //    热更链路连第一段都过不去），表现成"热更坏了"。
            //    第一版就是这么写的，被 hot_smoke 的 ① 报错抓出来的。
            // 热更 DLL 必须按**原生文件**打包：
            // 它们不是 Unity 资源，只是需要原样送到运行期的一段字节。
            // PackRawFile 是唯一能让 LoadBytesAsync / LoadTextAsync 读到原文的规则。
            EnsureCollector(group, HotUpdateFolder, nameof(PackRawFile), report);

            // 清理历史遗留：如果磁盘上的配置里已经有一条 AOT 子收集器（第一版留下的），
            // 这里主动删掉。否则光改代码不生效 —— 配置是**数据**，不会跟着代码回滚。
            RemoveNestedCollectors(group, HotUpdateFolder, report);
        }

        /// <summary>
        /// 删掉某个目录**之下**的多余收集器。
        /// </summary>
        /// <remarks>
        /// ⚠ 必须主动清理，理由：收集器配置是**持久化数据**（AssetBundleCollectorSetting.asset），
        ///   它不会因为代码改了而自动回滚。上一版代码写坏的那条配置会一直留在磁盘上，
        ///   继续让模拟构建失败 —— 而代码看起来已经完全正确了，非常难理解。
        ///   凡是"曾经写坏过配置"的坑，修代码的同时都要带一段清理逻辑。
        /// </remarks>
        private static void RemoveNestedCollectors(AssetBundleCollectorGroup group,
            string parentFolder, List<string> report)
        {
            string prefix = parentFolder.TrimEnd('/') + "/";

            // 先快照再删：边遍历边改集合会漏项。
            var doomed = new List<AssetBundleCollector>();
            foreach (AssetBundleCollector collector in group.Collectors)
            {
                string path = collector.CollectPath?.Replace('\\', '/');
                if (string.IsNullOrEmpty(path)) continue;

                // 只删子目录，不删父目录自己。
                if (path.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    doomed.Add(collector);
                }
            }

            foreach (AssetBundleCollector collector in doomed)
            {
                AssetBundleCollectorSettingData.RemoveCollector(group, collector);
                report.Add($"✂ 删除嵌套收集器：{collector.CollectPath}"
                           + "（父收集器已递归覆盖它，留着会让同一批文件被收两遍）");
            }
        }

        /// <summary>
        /// 目录里有没有至少一个文件（递归，忽略 .meta 与空目录）。
        /// </summary>
        /// <remarks>
        /// ⚠ 必须忽略 <c>.meta</c>：Unity 会给每个目录生成 `.meta`，
        ///   如果把它们算作"有内容"，那么"空目录判断"永远为真，本方法就白写了。
        ///   这是个很容易漏的点 —— 空目录里也躺着一个 `.meta`。
        /// </remarks>
        public static bool HasAnyFile(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return false;
            }

            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (path.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                return true;
            }

            return false;
        }

        private static AssetBundleCollectorPackage FindPackage(AssetBundleCollectorSetting setting, string name)
        {
            foreach (AssetBundleCollectorPackage package in setting.Packages)
            {
                if (package.PackageName == name) return package;
            }
            return null;
        }

        private static AssetBundleCollectorGroup FindGroup(AssetBundleCollectorPackage package, string name)
        {
            foreach (AssetBundleCollectorGroup group in package.Groups)
            {
                if (group.GroupName == name) return group;
            }
            return null;
        }

        private static void EnsureCollector(AssetBundleCollectorGroup group, string folder,
            string packRuleName, List<string> report)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                report.Add($"⚠ 目录不存在，跳过收集器：{folder}");
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(folder);

            foreach (AssetBundleCollector existing in group.Collectors)
            {
                if (existing.CollectPath != folder) continue;

                // 已存在。只纠正规则，不重建 —— 重建会把别人在面板上
                // 手动调过的标签、UserData 一起抹掉。
                bool changed = false;
                if (existing.PackRuleName != packRuleName) { existing.PackRuleName = packRuleName; changed = true; }
                if (existing.CollectorType != ECollectorType.MainAssetCollector)
                {
                    existing.CollectorType = ECollectorType.MainAssetCollector; changed = true;
                }
                if (existing.FilterRuleName != nameof(CollectAll))
                {
                    existing.FilterRuleName = nameof(CollectAll); changed = true;
                }
                if (existing.CollectorGUID != guid) { existing.CollectorGUID = guid; changed = true; }

                report.Add(changed
                    ? $"↻ 收集器规则已纠正：{folder} → {packRuleName}"
                    : $"＝ 收集器已就位：{folder} → {packRuleName}");
                return;
            }

            var collector = new AssetBundleCollector
            {
                CollectPath = folder,
                // ⚠ CollectorGUID 必须填：YooAsset 靠它识别"这个路径是目录还是文件"。
                //   留空时目录会被当成文件收集，结果是收集到 0 个资源，
                //   而且不报错 —— 只会在构建日志里显示资源数为 0。
                CollectorGUID = guid,
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = packRuleName,
                FilterRuleName = nameof(CollectAll),
                AssetTags = string.Empty,
                UserData = string.Empty,
            };

            AssetBundleCollectorSettingData.CreateCollector(group, collector);
            report.Add($"＋ 新建收集器：{folder} → {packRuleName}");
        }

        // ==================================================================
        //  状态报告
        // ==================================================================

        /// <summary>
        /// 打印当前收集器配置 + 模拟构建产物状态。
        /// </summary>
        public static string[] Status()
        {
            var report = new List<string>();

            AssetBundleCollectorSetting setting = AssetBundleCollectorSettingData.Setting;
            if (setting == null)
            {
                report.Add("❌ 取不到 AssetBundleCollectorSetting。");
                return report.ToArray();
            }

            report.Add($"配置文件：{SettingAssetPath()}");

            // ⚠ 先报「内存 vs 磁盘」是否一致。
            //   这一行是为了让那种"内存里看着对、磁盘上是空的"的假阳性
            //   在自检阶段就暴露，而不是等到进 Play 让 YooAsset 去取配置时才炸。
            report.Add(DescribePersistence());

            if (setting.Packages.Count == 0)
            {
                report.Add("⚠ 内存配置里一个包裹都没有。跑一次「万相/资源/初始化 YooAsset 收集器配置」。");
                return report.ToArray();
            }

            foreach (AssetBundleCollectorPackage package in setting.Packages)
            {
                report.Add($"包裹「{package.PackageName}」：地址可寻址={package.EnableAddressable}，" +
                           $"地址省略扩展名={package.SupportExtensionless}，" +
                           $"地址转小写={package.LocationToLower}，" +
                           $"组数={package.Groups.Count}");

                foreach (AssetBundleCollectorGroup group in package.Groups)
                {
                    report.Add($"  组「{group.GroupName}」：启用规则={group.ActiveRuleName}，" +
                               $"收集器={group.Collectors.Count}");

                    foreach (AssetBundleCollector collector in group.Collectors)
                    {
                        bool folderExists = AssetDatabase.IsValidFolder(collector.CollectPath);
                        report.Add($"    · {collector.CollectPath} " +
                                   $"[类型={collector.CollectorType}，" +
                                   $"地址={collector.AddressRuleName}，" +
                                   $"打包={collector.PackRuleName}，" +
                                   $"过滤={collector.FilterRuleName}，" +
                                   $"GUID={(string.IsNullOrEmpty(collector.CollectorGUID) ? "❌空" : "有")}，" +
                                   $"目录{(folderExists ? "存在" : "❌不存在")}]");
                    }

                    report.AddRange(ReportNestedCollectors(group));
                }
            }

            return report.ToArray();
        }

        /// <summary>
        /// 找出同一组里「一个收集器的目录在另一个收集器目录之下」的情况。
        /// </summary>
        /// <remarks>
        /// ⚠ 这是个**会打死整条资源链**的配置错误，而且报错信息与病因相距很远：
        ///     YooAsset 抛的是
        ///       The collecting asset file is existed : &lt;路径&gt; in group : &lt;组名&gt;
        ///     而你在资源初始化里看到的只是
        ///       EditorSimulateBuildPipeline build failed !
        ///   病因则是：收集器的 CollectAll 过滤器是**递归**的 ——
        ///   父目录的收集器已经把子目录里的文件收走了，再加一个子目录收集器，
        ///   同一批文件就被收两遍，YooAsset 直接拒绝。
        ///
        ///   本方法只**报告**不修改（Status 是只读命令），
        ///   修复交给 EnsureHotUpdateGroup 里的 RemoveNestedCollectors。
        /// </remarks>
        private static IEnumerable<string> ReportNestedCollectors(AssetBundleCollectorGroup group)
        {
            var lines = new List<string>();

            var paths = new List<string>();
            foreach (AssetBundleCollector c in group.Collectors)
            {
                string p = c.CollectPath?.Replace('\\', '/').TrimEnd('/');
                if (!string.IsNullOrEmpty(p))
                {
                    paths.Add(p);
                }
            }

            for (int i = 0; i < paths.Count; i++)
            {
                for (int j = 0; j < paths.Count; j++)
                {
                    if (i == j) continue;

                    // paths[i] 落在 paths[j] 之下 ⇒ 会重复收集
                    if (paths[i].StartsWith(paths[j] + "/", System.StringComparison.Ordinal))
                    {
                        lines.Add($"    ⚠⚠ 嵌套收集器：{paths[i]} 在 {paths[j]} 之下 —— "
                                  + "父收集器递归收集已覆盖它，会让同一批文件被收两遍，"
                                  + "资源初始化会以 EditorSimulateBuildPipeline build failed 失败。");
                    }
                }
            }

            return lines;
        }

        /// <summary>
        /// 描述配置的持久化状态：内存里有几个包裹、磁盘上有几个。
        /// </summary>
        /// <remarks>
        /// 「内存对、磁盘空」是这套收集器配置最阴的一种坏法（见 Setup 里的长注释）。
        /// 把两者并排打出来，一眼就能看出是没落盘还是真没配。
        /// </remarks>
        private static string DescribePersistence()
        {
            int memCount = AssetBundleCollectorSettingData.Setting?.Packages.Count ?? 0;

            string fullPath = Path.Combine(
                Application.dataPath,
                nameof(AssetBundleCollectorSetting) + ".asset");

            if (!File.Exists(fullPath))
            {
                return $"落盘状态：❌ 磁盘上没有 {SettingAssetPath()}（内存里 {memCount} 个包裹）";
            }

            string text = File.ReadAllText(fullPath);
            bool diskHasIt = text.Contains("PackageName: " + PackageName);

            if (memCount > 0 && !diskHasIt)
            {
                return $"落盘状态：❌ 内存 {memCount} 个包裹，但磁盘上没有「{PackageName}」" +
                       " —— 配置没落盘，YooAsset 读的是空文件。" +
                       "跑一次「万相/资源/初始化 YooAsset 收集器配置」即可修。";
            }

            return $"落盘状态：{(diskHasIt ? "✅" : "⚠")} 内存 {memCount} 个包裹，" +
                   $"磁盘{ (diskHasIt ? "含" : "不含") }「{PackageName}」" +
                   $"（AssetBundleCollectorSettingData.IsDirty={AssetBundleCollectorSettingData.IsDirty}）";
        }
    }
}
