// ============================================================================
//  万相 · 主界面 3D 场景层（混合方案：UI 仍 2D，背景层用 3D）
//  ---------------------------------------------------------------------------
//  2026-10-11 用户需求：
//    · 主界面要有"元素动起来"——出战异兽在四周走动
//    · 天气要有体积感（下雨、下雪等）
//    · 问能否把主界面转 3D
//
//  方案取舍（已与用户确认走**混合方案**）：
//    Canvas 保持 ScreenSpaceOverlay（UI 全 2D，不动现有布局）；
//    新增一台 **3D 正交相机 + 世界空间根节点** 放在 UI 之下：
//      · 背景 → 3D 平面（可做视差、雾、光照）
//      · 异兽 → SpriteRenderer 在 3D 空间走动，**Z 轴不同 ⇒ 真前后景深与遮挡**
//      · 天气 → Unity **ParticleSystem**（真粒子，有体积感/惯性/碰撞）
//    若整站转 World Space Canvas 需重做全部 UI 适配且与其它 2D 面板割裂 ⇒ 未采用。
//
//  ⚠ 关键实现点：UIRoot 的 Canvas 是 ScreenSpaceOverlay，**不需要相机也能渲染**，
//   但要让 3D 内容显示在 UI 之下，必须让这层由自己的相机渲染，且相机 depth 小于
//   UI 所在相机（Overlay Canvas 不占相机，故 3D 相机设 depth = -10 即可）。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Presentation;   // SpriteCatalog 在这个命名空间（2026-10-11 漏 using 报 CS0246）

namespace WanXiang.Modules.UI
{
    /// <summary>主界面 3D 背景层：3D 相机 + 异兽行走 + 天气粒子。</summary>
    public sealed class HomeScene3D : MonoBehaviour
    {
        /// <summary>面板关闭时停驱动。</summary>
        public bool Running { get; set; }

        [Header("3D 相机")]
        [SerializeField] private Camera _camera;              // 自己建的 3D 正交相机
        [SerializeField] private float _orthoSize = 5.4f;     // 正交视野半高
        [SerializeField] private float _camZ = 10f;           // 相机 Z（决定可见深度范围）

        [Header("异兽行走")]
        [SerializeField] private SpriteCatalog _sprites;      // 立绘来源（注入）
        [SerializeField] private int _maxWalkers = 6;// 同屏最多几只
        [SerializeField] private float _walkSpeed = 0.55f;     // 世界单位/秒
        [SerializeField] private float _scaleBase = 1.05f;    // 立绘基准缩放

        [Header("天气粒子")]
        [SerializeField] private ParticleSystem _psRain;       // 雨
        [SerializeField] private ParticleSystem _psSnow;       // 雪

        private Camera _cam;
        private Transform _root;                 // 3D 内容根
        private readonly List<Walker> _walkers = new List<Walker>();
        private readonly List<string> _currentIds = new List<string>();

        private struct Walker
        {
            public Transform T;
            public SpriteRenderer Sr;
            public float Speed;
            public float Dir;          // +1 向右 / -1 向左
            public float BaseY;
            public float Z;
            public float Phase;
            public float Scale;
        }

        private void Awake()
        {
            EnsureCamera();
            EnsureRoot();
        }

        /// <summary>按出战异兽名单重建行走者（每次开面调，名单变了也调）。</summary>
        public void SetWalkers(IReadOnlyList<string> beastIds)
        {
            EnsureCamera();
            EnsureRoot();

            _currentIds.Clear();
            if (beastIds != null)
            {
                for (int i = 0; i < beastIds.Count && _currentIds.Count < _maxWalkers; i++)
                    if (!string.IsNullOrEmpty(beastIds[i])) _currentIds.Add(beastIds[i]);
            }

            // 清掉旧的
            for (int i = 0; i < _walkers.Count; i++)
                if (_walkers[i].T != null) Destroy(_walkers[i].T.gameObject);
            _walkers.Clear();

            if (_sprites == null) return;
            float halfW = _orthoSize * 16f / 9f;   // @16:9
            for (int i = 0; i < _currentIds.Count; i++)
            {
                var sp = _sprites.Get(_currentIds[i]);
                if (sp == null) continue;           // 缺图就跳过，不占位
                var go = new GameObject("Walker_" + _currentIds[i]);
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;

                // Z 分层：越靠前越大 ⇒ 真景深
                float z = -1.2f + i * 0.5f;
                float scale = _scaleBase * (1f + z * 0.10f);
                sr.sortingOrder = Mathf.RoundToInt(-z * 100f);   // Z 越小越靠前

                var rt = go.GetComponent<Transform>();
                rt.localScale = new Vector3(scale, scale, 1f);
                // 底枢轴（贴地）
                var b = sp.bounds;
                rt.position = new Vector3(Random.Range(-halfW, halfW), _orthoSize * 0.62f + b.center.y * scale, z);

                _walkers.Add(new Walker
                {
                    T = rt, Sr = sr,
                    Speed = _walkSpeed * Random.Range(0.75f, 1.25f),
                    Dir = Random.value > 0.5f ? 1f : -1f,
                    BaseY = rt.position.y,
                    Z = z,
                    Phase = Random.Range(0f, 6.28f),
                    Scale = scale,
                });
            }
            Debug.Log("[Home3D] 行走异兽 " + _walkers.Count + " 只 /名单 " + _currentIds.Count);
        }

        /// <summary>天气（0晴 1雨 2雪 3阴）。用 Unity 粒子系统做，体积感比 Image 强。</summary>
        public void SetWeather(int kind)
        {
            if (_psRain != null) _psRain.gameObject.SetActive(kind == 1);
            if (_psSnow != null) _psSnow.gameObject.SetActive(kind == 2);
            // 阴天：调环境光/背景色（背景层没光也生效，靠雾色）
            if (_cam != null)
            {
                var bg = _cam.backgroundColor;
                switch (kind)
                {
                    case 2: bg = new Color(0.62f, 0.64f, 0.66f); break;
                    case 1: bg = new Color(0.72f, 0.75f, 0.78f); break;
                    default: bg = new Color(0.86f, 0.87f, 0.86f); break;
                }
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = bg;
            }
            Debug.Log("[Home3D] 天气 kind=" + kind);
        }

        private void Update()
        {
            if (!Running) return;
            float dt = Time.deltaTime;
            float halfW = _orthoSize * 16f / 9f;
            float t = Time.time;
            for (int i = 0; i < _walkers.Count; i++)
            {
                var w = _walkers[i];
                if (w.T == null) continue;
                // 沿 X 走，出界换向
                var p = w.T.position;
                p.x += w.Dir * w.Speed * dt;
                if (p.x > halfW + 0.8f) { p.x = -halfW - 0.8f; w.Dir = 1f; }
                if (p.x < -halfW - 0.8f) { p.x = halfW + 0.8f; w.Dir = -1f; }
                // 轻微上下浮动（走路起伏）
                p.y = w.BaseY + Mathf.Sin(t * 3.2f + w.Phase) * 0.05f;
                w.T.position = p;
                // 朝向翻转
                w.T.localScale = new Vector3(w.Scale * w.Dir, w.Scale, 1f);
                _walkers[i] = w;
            }
        }

        private void EnsureCamera()
        {
            if (_cam != null) return;
            _cam = _camera;
            if (_cam == null)
            {
                var go = new GameObject("HomeCamera3D");
                go.transform.SetParent(transform, false);
                _cam = go.AddComponent<Camera>();
                _camera = _cam;
            }
            _cam.orthographic = true;
            _cam.orthographicSize = _orthoSize;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.86f, 0.87f, 0.86f);
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 100f;
            // ⛔ depth 必须为负：UI 是 Overlay Canvas（不占相机），但 3D 相机若为 0 会
            //   与场景相机抢占主画面。设 -10 让它稳定垫底。
            _cam.depth = -10f;
            _cam.transform.position = new Vector3(0f, 0f, _camZ);
            _cam.transform.rotation = Quaternion.identity;
            _cam.transform.localScale = Vector3.one;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            var go = new GameObject("HomeWorld3D");
            go.transform.SetParent(transform, false);
            _root = go.transform;
        }

        /// <summary>注入立绘目录（HomePanel 在 OnCreate 里调）。</summary>
        public void InjectCatalog(SpriteCatalog cat) { _sprites = cat; }
    }
}