// ============================================================================
//  万相 · UI 框架 · YooAsset 面板加载器
//  ---------------------------------------------------------------------------
//  IUIPanelLoader 的正式实现，替代原型期的 ResourcesPanelLoader
//  （见 Framework/UI/ResourcesPanelLoader.cs）。
//
//  它刻意做得很薄：**只负责 key → location 的映射**。
//  引用计数完全交给 IResourceService 单点维护 ——
//  如果这里再自己维护一份，就会出现「两套计数各说各话」的情况，
//  而引用计数算错的症状（随机丢贴图、内存不落）极难排查。
//  单一事实来源比省一次字典查找重要得多。
//
//  ⚠ key → location 的映射规则：
//    默认情况下 **key 就是资源地址**（prefix / suffix 都留空）。
//    这要求 YooAsset 收集器里该项的地址规则是 AddressByFileName
//    （取文件名，不含扩展名），并且包裹勾了 SupportExtensionless。
//    这样面板类上的 [UIPanel("Panel_Login")] 直接对应
//    收集到的文件 Panel_Login.prefab —— 中间没有任何需要记住的约定。
//
//    如果工程里资源地址不是文件名（比如用了完整的 Assets/... 路径），
//    就把 prefix 设成 {工程路径前缀}，suffix 设成 ".prefab"。
//    但更推荐改收集器的地址规则：少一层映射就少一处能写错的地方。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WanXiang.Framework.ResourceSystem;
using WanXiang.Framework.UI;

namespace WanXiang.Framework.ResourceSystem.Backend
{
    /// <summary>
    /// 走 <see cref="IResourceService"/> 的面板加载器。
    /// </summary>
    public sealed class YooAssetPanelLoader : IUIPanelLoader
    {
        private readonly IResourceService _resources;
        private readonly string _prefix;
        private readonly string _suffix;

        /// <summary>key → 实际 location，避免每次都做字符串拼接。面板数量有限，字典很小。</summary>
        private readonly Dictionary<string, string> _locations =
            new Dictionary<string, string>(64, StringComparer.Ordinal);

        /// <param name="resources">资源服务。通常是 ResourceBootstrap.Resource。</param>
        /// <param name="prefix">地址前缀。留空表示 key 本身就是地址。</param>
        /// <param name="suffix">地址后缀（例如 ".prefab"）。留空表示地址不含扩展名。</param>
        public YooAssetPanelLoader(IResourceService resources, string prefix = null, string suffix = null)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _prefix = prefix ?? string.Empty;
            _suffix = suffix ?? string.Empty;
        }

        public UniTask<GameObject> LoadPanelPrefabAsync(string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[UI] LoadPanelPrefabAsync 收到空 key。");
                return UniTask.FromResult<GameObject>(null);
            }

            if (_resources == null || !_resources.IsReady)
            {
                Debug.LogError(
                    "[UI] 资源系统尚未就绪，无法加载面板资源。\n" +
                    "检查场景里是否有 ResourceBootstrap，且它初始化成功（看控制台有没有 [资源] 开头的报错）。");
                return UniTask.FromResult<GameObject>(null);
            }

            return _resources.LoadAssetAsync<GameObject>(ResolveLocation(key), ct);
        }

        public void ReleasePanelPrefab(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_resources == null) return;

            _resources.ReleaseAsset(ResolveLocation(key));
        }

        private string ResolveLocation(string key)
        {
            if (!_locations.TryGetValue(key, out string location))
            {
                location = _prefix + key + _suffix;
                _locations[key] = location;
            }
            return location;
        }
    }
}
