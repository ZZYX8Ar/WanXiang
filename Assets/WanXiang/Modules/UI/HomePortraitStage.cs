// ============================================================================
//  HomePortraitStage —— 主界面「看板/立绘」模式的动态立绘播放层
//  ---------------------------------------------------------------------------
//  设计文档：《万相_主界面动态立绘_设计文档.md》v1 方案 =
//    AI 图生视频 → StreamingAssets/LivePortraits/live_<beastId>.mp4 → VideoPlayer。
//
//  · VideoPlayer + RenderTexture + RawImage（isLooping；无音轨也安全，audioOutputMode=None）；
//  · 视频缺失 → 回退同名 png/jpg 静态图；再缺 → 只清屏留名字（不报错，方便先跑链路）；
//  · 自动轮换（PlayerPrefs：开关/间隔/顺序）+ 手动左右切换 + 「隐藏界面」；
//  · 画面比例用 AspectRatioFitter（FitWithinParent），黑底补边，不拉伸变形。
//
//  ⚠ 常驻复用纪律（HomePanel 是 Resident 面板）：Enter() 每次进入都完整重置
//    计时器 / 索引 / 隐藏态 —— 与 BattlePanel._halted 同源的坑，别再踩。
//  ⚠ 禁 ParticleSystem 铁律不涉及本层：动态表现全部来自视频本体。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace WanXiang.Modules.UI
{
    public sealed class HomePortraitStage : MonoBehaviour
    {
        [SerializeField] private RawImage _stage;        // Raw_Stage（视频/静态图都画在这里）
        [SerializeField] private TMP_Text _tmpName;      // Tmp_Name（左下：异兽名 · 典籍出处）
        [SerializeField] private CanvasGroup _uiLayer;   // UI_Layer（隐藏界面时整体淡出）
        [SerializeField] private GameObject _tapCatch;   // Img_TapCatch（UI 隐藏期接管点击，点任意处恢复）
        [SerializeField] private Button _btnPrev;        // Btn_Prev
        [SerializeField] private Button _btnNext;        // Btn_Next
        [SerializeField] private Button _btnHide;        // Btn_HideUI

        /// <summary>id → 展示名（"句芒 ·《山海经·海外东经》"）。由 HomePanel 注入。</summary>
        public System.Func<string, string> NameResolver;

        /// <summary>手动切换/自动轮换换片后回调（id）。HomePanel 用它记忆看板兽（仅手动切换落盘）。</summary>
        public event System.Action<string, bool> ShownChanged;

        private VideoPlayer _player;
        private RenderTexture _rt;
        private Texture _staticTex;                        // 静态图兜底的纹理（换片时销毁，防泄漏）
        private readonly List<string> _ids = new List<string>();
        private int _idx;
        private bool _uiHidden;
        private bool _wired;
        private float _rotateTimer;
        private float _rotateSeconds = 30f;
        private bool _autoRotate = true;
        private bool _randomOrder = false;

        private const string PrefAutoRotate = "PortraitAutoRotate";
        private const string PrefRotateSeconds = "PortraitRotateSeconds";
        private const string PrefRandom = "PortraitRotateRandom";

        public int UnlockedCount => _ids.Count;
        public string CurrentId => _ids.Count == 0 ? "" : _ids[_idx];

        private void Awake()
        {
            // ⚠ RawImage 的 color 会乘进纹理：调成黑 = 把视频涂黑。
            //   "准备期别闪白"用禁用图形实现（黑底 Img_Bg 在下面兜着），color 恒白。
            if (_stage != null) { _stage.color = Color.white; _stage.enabled = false; }
            _autoRotate = PlayerPrefs.GetInt(PrefAutoRotate, 1) == 1;
            _rotateSeconds = PlayerPrefs.GetFloat(PrefRotateSeconds, 30f);
            if (_rotateSeconds < 5f) _rotateSeconds = 30f;
            _randomOrder = PlayerPrefs.GetInt(PrefRandom, 0) == 1;
            WireButtons();
        }

        private void WireButtons()
        {
            if (_wired) return;
            _wired = true;
            if (_btnPrev != null) _btnPrev.onClick.AddListener(() => Step(-1));
            if (_btnNext != null) _btnNext.onClick.AddListener(() => Step(1));
            if (_btnHide != null) _btnHide.onClick.AddListener(ToggleUi);
            if (_tapCatch != null)
            {
                var b = _tapCatch.GetComponent<Button>();
                if (b != null) b.onClick.AddListener(ToggleUi);
            }
        }

        /// <summary>进入立绘展示（每次进入完整重置）。preferredId = 上次看板兽。
        /// ⚠ 只把**有视频资产**的异兽放进轮换序列 —— 没做视频的不进来（避免白屏轮播）；
        ///   若全部都没视频则退化为全列表（至少名字/解锁状态可见）。</summary>
        public void Enter(List<string> unlockedIds, string preferredId)
        {
            _ids.Clear();
            if (unlockedIds != null)
            {
                foreach (var id in unlockedIds)
                    if (File.Exists(VideoPathOf(id))) _ids.Add(id);
                if (_ids.Count == 0) _ids.AddRange(unlockedIds);
            }
            _idx = 0;
            if (!string.IsNullOrEmpty(preferredId))
            {
                int at = _ids.IndexOf(preferredId);
                if (at >= 0) _idx = at;
            }
            _uiHidden = false;
            ApplyUiHidden();
            _rotateTimer = 0f;
            PlayCurrent(false);
        }

        /// <summary>离开：停视频、释放 RenderTexture、清状态（HomePanel.OnDisable 也调）。</summary>
        public void Leave()
        {
            if (_player != null)
            {
                _player.Stop();
                _player.targetTexture = null;
            }
            _stage.texture = null;
            if (_stage != null) _stage.enabled = false;
            ReleaseTextures();
            _ids.Clear();
            _idx = 0;
            _rotateTimer = 0f;
            _uiHidden = false;
            ApplyUiHidden();
        }

        /// <summary>左右切换（dir = ±1）。手动切换会重置轮换计时并回调 ShownChanged(manual=true)。</summary>
        public void Step(int dir)
        {
            if (_ids.Count == 0) return;
            _idx = (_idx + dir + _ids.Count) % _ids.Count;
            _rotateTimer = 0f;
            PlayCurrent(true);
        }

        public void ToggleUi()
        {
            _uiHidden = !_uiHidden;
            _rotateTimer = 0f;
            ApplyUiHidden();
        }

        private void Update()
        {
            if (_ids.Count < 2 || !_autoRotate) return;
            // ⚠ 单帧增量钳到 1s：编辑器失焦/MCP 工具调用会产生巨大 unscaledDeltaTime，
            //   不钳的话轮换计时器一帧跨过 30s，玩家 alt-tab 回来也会连跳好几只。
            _rotateTimer += Mathf.Min(Time.unscaledDeltaTime, 1f);
            if (_rotateTimer < _rotateSeconds) return;
            _rotateTimer = 0f;
            if (_randomOrder) _idx = Random.Range(0, _ids.Count);
            else _idx = (_idx + 1) % _ids.Count;
            PlayCurrent(false);
        }

        // ------------------------------------------------------------ 播放

        private static string VideoPathOf(string id)
            => Path.Combine(Application.streamingAssetsPath, "LivePortraits", "live_" + id + ".mp4");

        private void PlayCurrent(bool manual)
        {
            if (_ids.Count == 0) return;
            string id = _ids[_idx];
            string video = VideoPathOf(id);

            if (File.Exists(video))
            {
                EnsurePlayer();
                string url = "file://" + video.Replace("\\", "/");
                // ⛔ 热切换坑：正在播/正在准备时直接改 url 会把播放器卡进"永不 Prepare"的僵死态
                //   （实测：第一次能播，轮换/切换后 isPrepared 永远 False 且无任何 error 回调）。
                //   换片前必须先 Stop；同一条且已就绪则不动它（重进看板不从头重播）。
                bool sameReady = _player.url == url && _player.isPrepared;
                if (!sameReady)
                {
                    if (_player.isPlaying || _player.isPrepared) _player.Stop();
                    ReleaseTextures();
                    if (_stage != null) _stage.enabled = false;   // 准备期不画（黑底兜着，防闪白）
                    _player.url = url;
                    _player.Prepare();          // prepareCompleted 里建 RT、设比例、Play
                }
                else if (_stage != null && _stage.texture != null)
                {
                    _stage.enabled = true;      // 同一条视频还在：直接恢复显示
                }
            }
            else
            {
                // 视频缺失 → 静态图兜底 → 再没有就清屏（只留名字，链路先跑通）
                if (_player != null) _player.Stop();
                ReleaseTextures();
                _staticTex = LoadStaticTexture(id);
                _stage.texture = _staticTex;
                if (_stage != null) _stage.enabled = _staticTex != null;
                Debug.LogWarning("[PortraitStage] 缺视频，用静态图兜底：" + id);
            }

            if (_tmpName != null)
            {
                string label = NameResolver != null ? NameResolver(id) : id;
                _tmpName.text = label;
            }
            ShownChanged?.Invoke(id, manual);
        }

        private void EnsurePlayer()
        {
            if (_player == null)
            {
                _player = GetComponent<VideoPlayer>();
                if (_player == null) _player = gameObject.AddComponent<VideoPlayer>();
                _player.playOnAwake = false;
                _player.renderMode = VideoRenderMode.RenderTexture;
                _player.audioOutputMode = VideoAudioOutputMode.None;
                _player.isLooping = true;
                _player.waitForFirstFrame = true;
                _player.prepareCompleted += OnPrepared;
                _player.errorReceived += OnVideoError;
            }
        }

        private void OnPrepared(VideoPlayer vp)
        {
            int w = (int)vp.width, h = (int)vp.height;
            if (w <= 0 || h <= 0 || _stage == null) return;
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                ReleaseTextures();
                _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "PortraitRT" };
            }
            _stage.texture = _rt;
            _stage.enabled = true;                       // 首帧就绪才显示（防准备期闪白）
            vp.targetTexture = _rt;
            var fitter = _stage.GetComponent<AspectRatioFitter>();
            if (fitter != null) fitter.aspectRatio = (float)w / h;
            if (!vp.isPlaying) vp.Play();
        }

        private void OnVideoError(VideoPlayer vp, string message)
        {
            Debug.LogError("[PortraitStage] 视频播放失败：" + message);
            ReleaseTextures();
            if (_ids.Count > 0) _stage.texture = _staticTex = LoadStaticTexture(_ids[_idx]);
        }

        private Texture LoadStaticTexture(string id)
        {
            string[] candidates =
            {
                Path.Combine(Application.streamingAssetsPath, "LivePortraits", "live_" + id + ".png"),
                Path.Combine(Application.streamingAssetsPath, "LivePortraits", "live_" + id + ".jpg"),
            };
            foreach (var p in candidates)
            {
                if (!File.Exists(p)) continue;
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(File.ReadAllBytes(p))) return tex;
                Destroy(tex);
            }
            return null;
        }

        private void ReleaseTextures()
        {
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_staticTex != null) { Destroy(_staticTex); _staticTex = null; }
        }

        private void ApplyUiHidden()
        {
            if (_uiLayer != null)
            {
                _uiLayer.alpha = _uiHidden ? 0f : 1f;
                _uiLayer.interactable = !_uiHidden;
                _uiLayer.blocksRaycasts = !_uiHidden;
            }
            if (_tapCatch != null) _tapCatch.SetActive(_uiHidden);
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.prepareCompleted -= OnPrepared;
                _player.errorReceived -= OnVideoError;
            }
            ReleaseTextures();
        }
    }
}
