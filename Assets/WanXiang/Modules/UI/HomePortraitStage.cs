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
using UnityEngine.EventSystems;
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

        // ---- 双播放器 A/B 双缓冲（2026-11-04）----
        //  ⛔ 为什么不用 VideoPlayer.isLooping：Windows 上它在循环点会内部 Stop + seek 重启，
        //    解码器要 flush 并重建参考帧链 —— 实测就是用户说的「首尾相连卡一两帧」。
        //    （先试过纯编码优化：-bf 0 / 短 GOP，B=0 已确认，但用户复验"还是卡"。）
        //  ✅ 两个播放器各持一张 RenderTexture：当前片快到末尾时，另一个**已经 Prepare 好**
        //    并从 0 起播 2 帧；循环点只把 RawImage.texture 换过去 —— 全程零 seek。
        private VideoPlayer _player;                       // A（prefab 上的那个）
        private VideoPlayer _playerB;                      // B（运行时挂在子物体上）
        private RenderTexture _rt;                         // A 的 RT
        private RenderTexture _rtB;                        // B 的 RT
        private VideoPlayer _active;                       // 当前正在播（A 或 B）
        private bool _swapping;                            // 已提前启动备用，等 loopPointReached 换纹理
        private string _currentUrl;                        // 当前片源（备用重新 Prepare 用）
        private Texture _staticTex;                        // 静态图兜底的纹理（换片时销毁，防泄漏）
        private readonly List<string> _ids = new List<string>();
        private int _idx;
        private bool _uiHidden;
        private bool _wired;
        private float _rotateTimer;
        private float _rotateSeconds = 180f;               // 默认约一首歌的时间（可在此面板调：60/120/180/240）
        private bool _autoRotate = true;

        // ---- 入场摄像机动画（虚拟镜头 = Raw_Stage 的缩放/位移/淡入）----
        //   按异兽站位逐兽配：句芒「居中偏右全身微仰」→ 从人物上半身特写缓缓拉远到全身。
        //   其他异兽做完视频后按站位在此补一行即可，没有配置的用默认推拉。
        private static readonly Dictionary<string, Vector3> EntrancePresets = new Dictionary<string, Vector3>
        {
            // x=startScale, y=起始位置x, z=起始位置y
            { "jumang", new Vector3(1.5f, 0f, -140f) },
        };
        private static readonly Vector3 DefaultEntrance = new Vector3(1.3f, 0f, -60f);
        private const float EntranceDefaultDur = 1.3f;
        private Vector3 _camFrom;          // x=scale, y/z=pos
        private float _camDur;
        private float _camT = -1f;         // -1 = 动画未在播
        private const float FadeInSec = 0.45f;

        // ---- 触屏互动（蔚蓝档案式）：长按注视跟随 + 点击回弹/白闪 ----
        //   视频是预渲染的，眼睛没法真动；用"整幅立绘朝鼠标轻微偏移+侧倾+微放大"
        //   制造注视错觉（长按时增强），点击 = 白闪一瞬 + 弹性回弹 + 立绘语音占位。
        [SerializeField] private Image _imgFlash;          // Img_Flash（点击白闪，α 常态 0，默认隐藏）
        private Vector2 _followPos;                        // 注视平滑位移
        private float _followTilt;                         // 注视侧倾（度）
        private float _followZoom = 1f;                    // 注视微放大
        private float _tapBounceT = -1f;                   // 点击回弹计时（-1 = 未在播）
        private float _flashT = -1f;                       // 白闪计时（-1 = 未在播）
        private const float FollowMaxOffset = 26f;         // 注视最大位移（px）
        private const float FollowMaxTilt = 2.2f;          // 注视最大侧倾（度）
        private const float HoldZoom = 1.025f;             // 长按注视微放大
        private const float FollowLerpRate = 8f;           // 跟随阻尼（越大越跟手）

        public int UnlockedCount => _ids.Count;
        public string CurrentId => _ids.Count == 0 ? "" : _ids[_idx];

        private void Awake()
        {
            // ⚠ RawImage 的 color 会乘进纹理：调成黑 = 把视频涂黑。
            //   "准备期别闪白"用禁用图形实现（黑底 Img_Bg 在下面兜着），color 恒白。
            if (_stage != null) { _stage.color = Color.white; _stage.enabled = false; }
            _autoRotate = PortraitBoardPrefs.AutoRotateOn;          // 开关/间隔在「看板库」面板设置
            _rotateSeconds = PortraitBoardPrefs.LoadSeconds();      // 默认 180 ≈ 一首歌的时间
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

        // ================================================================ 触屏互动（蔚蓝档案式）

        /// <summary>长按注视跟随 + 点击回弹/白闪。入场动画期间让位（不抢 transform）。</summary>
        private void UpdateTouchInteract()
        {
            if (_stage == null || _ids.Count == 0) return;
            bool entranceBusy = _camT >= 0f;
            var rt = (RectTransform)_stage.transform;

            // 命中判定：指针在视频画面内，且最顶层命中的不是 UI_Layer 里的真按钮。
            // ⛔ 不能用 IsPointerOverGameObject——Portrait_Root 的 Img_Bg/TapCatch 本身
            //   就是 raycastTarget=true，会让它永远为 true，交互全程被拦死（踩过）。
            bool overStage = false;
            if (_stage.enabled && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null))
            {
                var ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
                var hits = new List<RaycastResult>(4);
                EventSystem.current.RaycastAll(ped, hits);
                bool onButton = hits.Count > 0 && _uiLayer != null &&
                                hits[0].gameObject.transform.IsChildOf(_uiLayer.transform);
                overStage = !onButton;
            }

            // 点击反应（按下那一帧触发一次）
            if (overStage && !entranceBusy && Input.GetMouseButtonDown(0))
                OnPortraitTapped();

            // 长按注视：立绘朝鼠标方向轻微偏移 + 侧倾 + 微放大
            Vector2 tPos = Vector2.zero;
            float tTilt = 0f, tZoom = 1f;
            if (overStage && !entranceBusy && Input.GetMouseButton(0))
            {
                float nx = Mathf.Clamp((Input.mousePosition.x - Screen.width * 0.5f) / (Screen.width * 0.5f), -1f, 1f);
                float ny = Mathf.Clamp((Input.mousePosition.y - Screen.height * 0.5f) / (Screen.height * 0.5f), -1f, 1f);
                tPos = new Vector2(nx * FollowMaxOffset, ny * FollowMaxOffset * 0.6f);
                tTilt = -nx * FollowMaxTilt;
                tZoom = HoldZoom;
            }

            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * FollowLerpRate);
            if (entranceBusy)
            {
                _followPos = Vector2.zero; _followTilt = 0f; _followZoom = 1f;   // 入场结束后从干净姿态接管
                return;
            }
            _followPos = Vector2.Lerp(_followPos, tPos, k);
            _followTilt = Mathf.LerpAngle(_followTilt, tTilt, k);
            _followZoom = Mathf.Lerp(_followZoom, tZoom, k);

            // 点击回弹（乘在注视缩放之上）
            float bounce = 1f;
            if (_tapBounceT >= 0f)
            {
                _tapBounceT += Time.unscaledDeltaTime;
                float kb = Mathf.Clamp01(_tapBounceT / 0.35f);
                bounce = 1f + 0.05f * Mathf.Sin(Mathf.PI * kb) * (1f - kb);
                if (kb >= 1f) _tapBounceT = -1f;
            }

            rt.anchoredPosition = _followPos;
            rt.localScale = new Vector3(_followZoom, _followZoom, 1f) * bounce;
            rt.localEulerAngles = new Vector3(0f, 0f, _followTilt);
        }

        private void OnPortraitTapped()
        {
            _tapBounceT = 0f;
            if (_imgFlash != null) { _flashT = 0f; _imgFlash.gameObject.SetActive(true); }
            // 立绘语音占位：Resources/Audio/SFX/voice_<id> 不存在时静默跳过（AudioSystem 惯例）
            WanXiang.Framework.Audio.AudioSystem.PlaySfx("voice_" + CurrentId, 0.9f);
        }

        private void UpdateTapFlash()
        {
            if (_imgFlash == null || _flashT < 0f) return;
            _flashT += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_flashT / 0.28f);
            var c = _imgFlash.color;
            c.a = 0.16f * (1f - k);
            _imgFlash.color = c;
            if (k >= 1f) { _flashT = -1f; _imgFlash.gameObject.SetActive(false); }
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
            StopAllPlayers();
            if (_stage != null) { _stage.texture = null; _stage.enabled = false; }
            _currentUrl = null;
            ReleaseTextures();
            _ids.Clear();
            _idx = 0;
            _rotateTimer = 0f;
            _uiHidden = false;
            _camT = -1f;
            _tapBounceT = -1f;
            _flashT = -1f;
            _followPos = Vector2.zero;
            _followTilt = 0f;
            _followZoom = 1f;
            if (_imgFlash != null) { var fc = _imgFlash.color; fc.a = 0f; _imgFlash.color = fc; _imgFlash.gameObject.SetActive(false); }
            if (_stage != null)
            {
                var rt = (RectTransform)_stage.transform;   // 摄像机动画/注视状态复位（常驻复用纪律）
                rt.localScale = Vector3.one;
                rt.anchoredPosition = Vector2.zero;
                rt.localEulerAngles = Vector3.zero;
                _stage.color = Color.white;
            }
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
            // 入场摄像机动画推进（easeOutCubic 拉回）
            if (_camT >= 0f && _stage != null)
            {
                _camT += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_camT / _camDur);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                var rt = (RectTransform)_stage.transform;
                float s = Mathf.Lerp(_camFrom.x, 1f, e);
                rt.localScale = new Vector3(s, s, 1f);
                rt.anchoredPosition = Vector2.LerpUnclamped(
                    new Vector2(_camFrom.y, _camFrom.z), Vector2.zero, e);
                float a = Mathf.Clamp01(_camT / FadeInSec);
                _stage.color = new Color(1f, 1f, 1f, a);
                if (k >= 1f) { _camT = -1f; _stage.color = Color.white; }
            }

            UpdateTouchInteract();
            UpdateTapFlash();
            UpdateDoubleBuffer();   // 循环点交接（A/B 双缓冲，零 seek）

            if (_ids.Count < 2 || !_autoRotate) return;
            // ⚠ 单帧增量钳到 1s：编辑器失焦/MCP 工具调用会产生巨大 unscaledDeltaTime，
            //   不钳的话轮换计时器一帧跨过间隔，玩家 alt-tab 回来也会连跳好几只。
            _rotateTimer += Mathf.Min(Time.unscaledDeltaTime, 1f);
            if (_rotateTimer < _rotateSeconds) return;
            _rotateTimer = 0f;
            _idx = (_idx + 1) % _ids.Count;
            PlayCurrent(false);
        }

        /// <summary>入场摄像机动画：按异兽站位取预设（无配置用默认推拉）。</summary>
        private void PlayEntrance(string id)
        {
            if (_stage == null) return;
            Vector3 preset;
            float dur = EntranceDefaultDur;
            if (!EntrancePresets.TryGetValue(id, out preset))
            {
                preset = DefaultEntrance;
                dur = EntranceDefaultDur;
            }
            else
            {
                dur = EntranceDefaultDur;
            }
            _camFrom = preset;
            _camDur = Mathf.Max(0.6f, dur);
            _camT = 0f;
            var rt = (RectTransform)_stage.transform;
            rt.localScale = new Vector3(preset.x, preset.x, 1f);
            rt.anchoredPosition = new Vector2(preset.y, preset.z);
            _stage.color = new Color(1f, 1f, 1f, 0f);
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
                bool sameReady = _active != null && _active.url == url && _active.isPrepared;
                if (!sameReady)
                {
                    StopAllPlayers();
                    ReleaseTextures();
                    if (_stage != null) _stage.enabled = false;   // 准备期不画（黑底兜着，防闪白）
                    _currentUrl = url;
                    _active = _player;
                    _player.url = url;
                    _player.Prepare();          // prepareCompleted 里建 RT、设比例、Play，并顺带准备 B
                }
                else if (_stage != null && _stage.texture != null)
                {
                    _stage.enabled = true;      // 同一条视频还在：直接恢复显示
                }
            }
            else
            {
                // 视频缺失 → 静态图兜底 → 再没有就清屏（只留名字，链路先跑通）
                StopAllPlayers();
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
            PlayEntrance(id);                    // 每次换片/进入都有入场摄像机动画（按站位预设）
            ShownChanged?.Invoke(id, manual);
        }

        private void EnsurePlayer()
        {
            if (_player == null)
            {
                _player = GetComponent<VideoPlayer>();
                if (_player == null) _player = gameObject.AddComponent<VideoPlayer>();
                ConfigurePlayer(_player);
                _player.prepareCompleted += OnPrepared;
                _player.errorReceived += OnVideoError;
                _player.loopPointReached += OnLoopPoint;   // isLooping=false：靠它做 A/B 交接
                _active = _player;
            }
            if (_playerB == null)
            {
                // B 挂在子物体上（同一物体挂两个 VideoPlayer 虽然可行，但子物体更好排查）
                var go = new GameObject("PortraitVideoB");
                go.transform.SetParent(transform, false);
                _playerB = go.AddComponent<VideoPlayer>();
                ConfigurePlayer(_playerB);
                _playerB.prepareCompleted += OnPrepared;
                _playerB.errorReceived += OnVideoError;
                _playerB.loopPointReached += OnLoopPoint;
            }
        }

        /// <summary>A/B 共用配置。⛔ isLooping 必须 false —— 循环交给 OnLoopPoint 换纹理。</summary>
        private void ConfigurePlayer(VideoPlayer vp)
        {
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.isLooping = false;
            vp.waitForFirstFrame = true;   // 备用必须"帧已在纹理里"，否则切过去可能空一帧
            if (vp.canSetSkipOnDrop) vp.skipOnDrop = true;
            if (vp.canSetTimeUpdateMode) vp.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        }

        private void OnPrepared(VideoPlayer vp)
        {
            int w = (int)vp.width, h = (int)vp.height;
            if (w <= 0 || h <= 0 || _stage == null) return;
            var rt = EnsureRtFor(vp, w, h);
            vp.targetTexture = rt;

            if (vp != _active)
            {
                // 备用：已就绪，停在首帧等切换（不 Play，省一路解码）
                return;
            }

            _stage.texture = rt;
            _stage.enabled = true;                       // 首帧就绪才显示（防准备期闪白）
            var fitter = _stage.GetComponent<AspectRatioFitter>();
            if (fitter != null) fitter.aspectRatio = (float)w / h;
            if (!vp.isPlaying) vp.Play();
            _swapping = false;
            StartCoroutine(PrepareStandbyAfter(0.8f));    // 主片先播稳，再把备用准备好
        }

        /// <summary>按播放器取/建 RT。A、B 各一张，切换时只是换引用。</summary>
        private RenderTexture EnsureRtFor(VideoPlayer vp, int w, int h)
        {
            bool isA = vp == _player;
            var cur = isA ? _rt : _rtB;
            if (cur == null || cur.width != w || cur.height != h)
            {
                if (cur != null) { cur.Release(); Destroy(cur); }
                cur = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    name = isA ? "PortraitRT_A" : "PortraitRT_B",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                if (isA) _rt = cur; else _rtB = cur;
            }
            return cur;
        }

        private RenderTexture RtOf(VideoPlayer vp) => vp == _player ? _rt : _rtB;

        /// <summary>主片播稳后把另一台准备好（Prepare 要几百 ms，8 秒片长留足余量）。</summary>
        private System.Collections.IEnumerator PrepareStandbyAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (string.IsNullOrEmpty(_currentUrl)) yield break;
            var standby = _active == _player ? _playerB : _player;
            if (standby == null || standby == _active) yield break;
            if (standby.isPrepared || standby.isPlaying) yield break;
            standby.url = _currentUrl;
            standby.Prepare();
        }

        /// <summary>
        /// 循环点交接：当前片到末尾时，把画面切到**已经起播 2 帧**的备用播放器。
        /// 全程没有 seek —— 这是「首尾相连卡一两帧」的根治点。
        /// </summary>
        private void OnLoopPoint(VideoPlayer vp)
        {
            if (vp == null || vp != _active) return;
            var other = vp == _player ? _playerB : _player;
            if (other != null && other.isPrepared)
            {
                var rt = RtOf(other);
                if (rt != null && _stage != null)
                {
                    _stage.texture = rt;
                    _stage.enabled = true;
                }
                _active = other;
                _swapping = false;
                vp.Stop();                                  // 退居备用，稍后重新 Prepare
                StartCoroutine(ReprepareAsStandby(vp));
            }
            else
            {
                // 备用没就绪（极端情况）→ 退回单机重播：宁可有 1 帧停顿也不要黑屏
                Debug.LogWarning("[PortraitStage] 双缓冲备用未就绪，退回原地重播");
                vp.frame = 0;
                vp.Play();
            }
        }

        private System.Collections.IEnumerator ReprepareAsStandby(VideoPlayer vp)
        {
            yield return new WaitForSecondsRealtime(0.35f);
            if (vp == null || vp == _active) yield break;
            if (string.IsNullOrEmpty(_currentUrl)) yield break;
            vp.url = _currentUrl;
            vp.Prepare();
        }

        /// <summary>当前片快到末尾时（剩 2 帧 ≈ 83ms）提前启动备用 —— 切换时它纹理里已有画面。</summary>
        private void UpdateDoubleBuffer()
        {
            if (_swapping || _active == null || !_active.isPrepared) return;
            // ⚠ frameCount 是 ulong、frame 是 long（Unity API 不对称，别想当然写 long fc）
            ulong fc = _active.frameCount;
            if (fc <= 6) return;                            // frameCount 不可信（部分容器返回 0）
            long frame = _active.frame;
            if (frame < 0) return;
            if ((ulong)frame < fc - 2) return;
            var other = _active == _player ? _playerB : _player;
            if (other == null || !other.isPrepared) return;
            if (!other.isPlaying) other.Play();
            _swapping = true;
        }

        private void StopAllPlayers()
        {
            if (_player != null) { _player.Stop(); _player.targetTexture = null; }
            if (_playerB != null) { _playerB.Stop(); _playerB.targetTexture = null; }
            _swapping = false;
        }

        private void OnVideoError(VideoPlayer vp, string message)
        {
            Debug.LogError("[PortraitStage] 视频播放失败：" + message);
            _swapping = false;
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
            if (_rtB != null) { _rtB.Release(); Destroy(_rtB); _rtB = null; }
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
