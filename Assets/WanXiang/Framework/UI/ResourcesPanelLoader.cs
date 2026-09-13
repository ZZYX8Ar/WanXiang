// ============================================================================
//  WanXiang · UI 框架 · Resources 面板加载器（原型期实现）
//  ---------------------------------------------------------------------------
//  这是 IUIPanelLoader 的一个最小实现：从 Resources 目录加载面板 Prefab。
//
//  它的存在意义是"让 UI 框架今天就能跑起来"：不需要先接好 YooAsset、
//  不需要先建 Addressables 分组、不需要先配 CDN，UI 就能开工。
//  等资源系统接好，写一个 YooAssetPanelLoader / AddressablesPanelLoader 替换掉，
//  UI 框架与所有业务面板一行都不用改。
//
//  正式项目请务必替换：Resources 目录的内容会被全部打进包体且无法卸载，
//  面板一多，包体和内存都会很难看。
//
//  引用计数说明：
//    同一面板 Prefab 被多个实例共享，只有在引用数归零时才从缓存放掉。
//    GameObject 类型的资源无法通过 UnloadAsset 真正释放，需要 UnloadUnusedAssets，
//    所以这里只做"逻辑释放"，真卸载交由资源系统的统一清理时机。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WanXiang.Framework.UI
{
    public sealed class ResourcesPanelLoader : IUIPanelLoader
    {
        /// <summary>默认根目录。面板 Prefab 放 Assets/Resources/UI/ 下即可。</summary>
        public const string DefaultRoot = "UI";

        private readonly string _root;
        private readonly Dictionary<string, GameObject> _prefabs =
            new Dictionary<string, GameObject>(32);
        private readonly Dictionary<string, int> _refCounts =
            new Dictionary<string, int>(32);
        private readonly Dictionary<string, UniTaskCompletionSource<GameObject>> _pending =
            new Dictionary<string, UniTaskCompletionSource<GameObject>>(StringComparer.Ordinal);

        /// <param name="root">Resources 下的子目录名。传 null 或 "" 表示直接在 Resources 根下找。</param>
        public ResourcesPanelLoader(string root = DefaultRoot)
        {
            _root = root ?? string.Empty;
        }

        public async UniTask<GameObject> LoadPanelPrefabAsync(string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[UI] LoadPanelPrefabAsync 收到空 key。");
                return null;
            }

            // ---- 已缓存：加一次引用，直接返回 ----
            if (_prefabs.TryGetValue(key, out var cached) && cached != null)
            {
                AddRef(key);
                return cached;
            }

            // ---- 正在加载：等同一个任务，不重复读取 ----
            // 这一步与 UISystem 的 _pendingOpens 是两道独立防线：
            // UISystem 拦的是"同一个面板类"，这里拦的是"同一个资源 key"
            // （比如两个不同面板类指向了同一个 Prefab，属于配置错误，但至少不该加载两次）。
            if (_pending.TryGetValue(key, out var pending) && pending != null)
            {
                AddRef(key);
                return await pending.Task;
            }

            var tcs = new UniTaskCompletionSource<GameObject>();
            _pending[key] = tcs;

            try
            {
                string path = string.IsNullOrEmpty(_root) ? key : _root + "/" + key;
                var request = Resources.LoadAsync<GameObject>(path);

                await request.ToUniTask(cancellationToken: ct);

                var prefab = request.asset as GameObject;
                if (prefab == null)
                {
                    Debug.LogError(
                        $"[UI] Resources.Load 失败：\"{path}\"。 " +
                        $"请确认 Prefab 位于 Assets/Resources/{_root}/ 下且文件名与 key 完全一致。");
                    tcs.TrySetResult(null);
                    return null;
                }

                _prefabs[key] = prefab;
                AddRef(key);

                tcs.TrySetResult(prefab);
                return prefab;
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                throw;
            }
            finally
            {
                _pending.Remove(key);
            }
        }

        public void ReleasePanelPrefab(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!_refCounts.TryGetValue(key, out var count)) return;

            count--;
            if (count < 0) count = 0;
            _refCounts[key] = count;

            if (count == 0)
            {
                // 逻辑释放：不再被面板实例引用。
                // 保留 _prefabs 里的引用是为了避免"关了又开"时重复 IO；
                // 若要做成严格的按需释放，把下面这行注释掉即可。
                _prefabs.Remove(key);
            }
        }

        /// <summary>立刻清空所有缓存引用（配合 Resources.UnloadUnusedAssets 使用）。</summary>
        public void Clear()
        {
            _prefabs.Clear();
            _refCounts.Clear();
            _pending.Clear();
        }

        /// <summary>当前缓存的 Prefab 数量，调试用。</summary>
        public int CachedCount => _prefabs.Count;

        private void AddRef(string key)
        {
            _refCounts.TryGetValue(key, out var c);
            _refCounts[key] = c + 1;
        }
    }
}
