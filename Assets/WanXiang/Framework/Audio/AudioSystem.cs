// ============================================================================
//  万相 · 音频系统（框架）
//  ---------------------------------------------------------------------------
//  设计照搬 QFramework AudioKit 的成熟做法（本工程不引用 QFramework —— 游戏代码零引用，
//  为音频单独引入会把架构劈成两半；AudioKit 的价值在设计而非代码）：
//    · Sound / Music **两路音量**，各自独立、立即生效、自动持久化
//    · 音效走**池化 AudioSource**（可同时播多声，不互相打断）
//    · 取不到音频资源 ， **静默跳过**（绝不报错、绝不卡流程）
//    · 任何地方都通过静态入口 `AudioSystem.*` 调用，不需要在场景里摆对象
//
//  资源约定（文件名 = id，丢进目录即生效）：
//    Resources/Audio/SFX/<id>   —— 音效（.wav/.mp3/.ogg，Unity 自动导入为 AudioClip）
//    Resources/Audio/BGM/<id>   —— 音乐
//
//  持久化：PlayerPrefs（设置类数据，和存档分开、不随档走）。
//  设置面板的 `Sld_Bgm` / `Sld_Sfx` 两个滑条已经预留好回调，接的就是这里。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Framework.Audio
{
    /// <summary>音频统一入口（静态，无需在场景里摆对象；首次调用自动建宿主）。</summary>
    public static class AudioSystem
    {
        private const string KeySfx = "wx_audio_sfx";
        private const string KeyBgm = "wx_audio_bgm";
        private const string KeyMute = "wx_audio_mute";

        /// <summary>音量发生变化（0~1，含静音开关）。设置面板/任何需要联动的 UI 订阅它。</summary>
        public static event Action<float, float, bool> VolumeChanged;

        private static float _sfx = 0.8f;
        private static float _bgm = 0.6f;
        private static bool _mute;
        private static bool _loaded;
        private static AudioHost _host;
        /// <summary>⛔ 建宿主期间的护栏： 会**同步**执行新组件的 Awake，
        /// 而那一刻  还没被赋值 ， Awake 里若再读 AudioSystem 的属性就会**无限递归**
        /// （实测：StackOverflowException）。有了它，创建过程中任何重入都会直接返回。</summary>
        private static bool _creatingHost;
        private static readonly Dictionary<string, AudioClip> _sfxCache = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, AudioClip> _bgmCache = new Dictionary<string, AudioClip>();

        // ---- 音量（立即生效 + 落盘）----
        public static float SfxVolume
        {
            get { EnsureLoaded(); return _sfx; }
            set { EnsureLoaded(); _sfx = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeySfx, _sfx); Apply(); }
        }

        public static float MusicVolume
        {
            get { EnsureLoaded(); return _bgm; }
            set { EnsureLoaded(); _bgm = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeyBgm, _bgm); Apply(); }
        }

        public static bool Muted
        {
            get { EnsureLoaded(); return _mute; }
            set { EnsureLoaded(); _mute = value; PlayerPrefs.SetInt(KeyMute, _mute ? 1 : 0); Apply(); }
        }

        /// <summary>把设置写回（设置面板关闭时调一次即可；音量 setter 里也会即时写）。</summary>
        public static void Save() { PlayerPrefs.Save(); }

        // ---- 播放 ----
        /// <summary>放一个音效。取不到资源就什么都不做（静默）。</summary>
        public static void PlaySfx(string id, float volumeScale = 1f)
        {
            if (string.IsNullOrEmpty(id)) return;
            EnsureLoaded();
            if (_host == null) return;
            var clip = LoadClip("SFX", id, _sfxCache);
            if (clip == null) return;
            _host.PlaySfx(clip, Mathf.Clamp01(volumeScale));
        }

        /// <summary>放背景音乐（同一时刻只有一首；重复调用同一首不会重启）。</summary>
        public static void PlayMusic(string id, bool loop = true)
        {
            if (string.IsNullOrEmpty(id)) return;
            EnsureLoaded();
            if (_host == null) return;
            var clip = LoadClip("BGM", id, _bgmCache);
            if (clip == null) return;
            _host.PlayMusic(clip, loop);
        }

        public static void StopMusic() { if (_host != null) _host.StopMusic(); }

        /// <summary>按 id 预热（可选）——大场景切场时提前加载，避免第一次播放卡一下。</summary>
        public static void Preload(string kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (kind == "BGM") LoadClip("BGM", id, _bgmCache);
            else LoadClip("SFX", id, _sfxCache);
        }

        // ---- 内部 ----
        private static AudioClip LoadClip(string kind, string id, Dictionary<string, AudioClip> cache)
        {
            AudioClip c;
            if (cache.TryGetValue(id, out c) && c != null) return c;
            c = Resources.Load<AudioClip>(ResolvePath(kind, id));
            // ⛔ 不缓存 null：图/音频还没放进去时查过一次，不该被记一辈子（本工程踩过这个坑）
            if (c != null) cache[id] = c;
            return c;
        }

        // ==================================================================
        //  音频路径解析（配置驱动）
        //  -------------------------------------------------------------------
        //  音频已按用途拆进子目录（BGM/Act、SFX/Battle…），路径不能再靠
        //  "Audio/{kind}/{id}" 硬拼。统一由 Resources/Data/audio.csv 描述 id → 相对路径：
        //      id,kind,category,file,volume,loop
        //      bgm_act1_spring,bgm,Act,BGM/Act/bgm_act1_spring,0.55,1
        //
        //  ⭐ 以后加音乐 / 换分类 / 挪目录：**只改这张表 + 文件本身**，不用动任何代码。
        //  ⚠ 表里查不到 id 时回退老路径（保证旧调用不至于突然哑掉）。
        // ==================================================================
        private static Dictionary<string, string> _pathById;

        /// <summary>某个 id 是否真能加载到音频（自检用）。</summary>
        public static bool HasAudio(string kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return Resources.Load<AudioClip>(ResolvePath(kind, id)) != null;
        }

        private static string ResolvePath(string kind, string id)
        {
            EnsureAudioTable();
            string p;
            if (_pathById != null && _pathById.TryGetValue(id, out p) && !string.IsNullOrEmpty(p))
                return "Audio/" + p;
            return "Audio/" + kind + "/" + id;   // 兜底：老结构（平铺在 BGM/ 或 SFX/ 下）
        }

        private static void EnsureAudioTable()
        {
            if (_pathById != null) return;
            _pathById = new Dictionary<string, string>();

            var ta = Resources.Load<TextAsset>("Data/audio");
            if (ta == null) return;   // 表缺失：全部走兜底路径

            var lines = ta.text.Replace("\r", "").Split('\n');
            for (int i = 1; i < lines.Length; i++)   // 第 0 行是表头
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                var cols = lines[i].Split(',');
                if (cols.Length < 4) continue;
                var id = cols[0].Trim();
                var rel = cols[3].Trim();
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(rel))
                    _pathById[id] = rel;
            }
        }

        private static void EnsureLoaded()
        {
            if (!_loaded)
            {
                _loaded = true;
                _sfx = PlayerPrefs.GetFloat(KeySfx, 0.8f);
                _bgm = PlayerPrefs.GetFloat(KeyBgm, 0.6f);
                _mute = PlayerPrefs.GetInt(KeyMute, 0) != 0;
            }
            if (_host == null && Application.isPlaying && !_creatingHost)
            {
                _creatingHost = true;
                try
                {
                    var go = new GameObject("[AudioSystem]");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _host = go.AddComponent<AudioHost>();   // ⚠ 这行会同步跑 Awake（见 _creatingHost 注释）
                    // ⚠ 场景里若**没有 AudioListener**，所有音效都是哑的（Unity 会警告 no audio listeners）。
                    //   只在确实没有时补一个 —— 重复添加会和相机上的监听器冲突并再警告。
                    if (UnityEngine.Object.FindObjectOfType<UnityEngine.AudioListener>() == null)
                    {
                        go.AddComponent<UnityEngine.AudioListener>();
                    }
                    Apply();                                // 音量在**建完之后**再应用，别让 Awake 去读静态属性
                }
                finally { _creatingHost = false; }
            }
        }

        private static void Apply()
        {
            if (_host != null) _host.ApplyVolumes(_mute ? 0f : _sfx, _mute ? 0f : _bgm);
            if (VolumeChanged != null) VolumeChanged(_sfx, _bgm, _mute);
        }
    }

    /// <summary>
    /// 音频宿主：一个 BGM 源 + 一组音效池。⛔ 只由 <see cref="AudioSystem"/> 创建，别在场景里手动摆。
    /// </summary>
    internal sealed class AudioHost : MonoBehaviour
    {
        private AudioSource _music;
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private int _next;

        private void Awake()
        {
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            for (int i = 0; i < 6; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.loop = false;
                s.playOnAwake = false;
                _pool.Add(s);
            }
            // ⛔ 这里**绝不能**读 AudioSystem.SfxVolume / MusicVolume：
            //   此刻静态字段尚未完成初始化（宿主正在被创建）， 会递归回 EnsureLoaded ， 栈溢出。
            //   音量统一由 AudioSystem 在宿主建好后调 Apply() 设置。
        }

        public void ApplyVolumes(float sfx, float bgm)
        {
            for (int i = 0; i < _pool.Count; i++) if (_pool[i] != null) _pool[i].volume = sfx;
            if (_music != null) _music.volume = bgm;
        }

        /// <summary>轮转取一个空闲源；都在响就抢最早那个（宁愿打断也别没声）。</summary>
        public void PlaySfx(AudioClip clip, float scale)
        {
            if (clip == null) return;
            AudioSource pick = null;
            for (int i = 0; i < _pool.Count; i++)
            {
                var s = _pool[(_next + i) % _pool.Count];
                if (s != null && !s.isPlaying) { pick = s; _next = (_next + i + 1) % _pool.Count; break; }
            }
            if (pick == null) { pick = _pool[_next]; _next = (_next + 1) % _pool.Count; }
            if (pick == null) return;
            pick.PlayOneShot(clip, Mathf.Clamp01(scale));
        }

        // 每首音乐上次播到哪儿 —— 界面来回切时接着放，而不是从头开始。
        // 用户 2026-11-07：从编队回节点地图，音乐从头播很难受。
        private readonly Dictionary<AudioClip, float> _musicPos = new Dictionary<AudioClip, float>();

        public void PlayMusic(AudioClip clip, bool loop)
        {
            if (clip == null || _music == null) return;
            if (_music.clip == clip && _music.isPlaying) return;   // 同一首不重启

            // 切走之前，把当前这首播到哪儿记下来
            if (_music.clip != null) _musicPos[_music.clip] = _music.time;

            _music.clip = clip;
            _music.loop = loop;
            _music.Play();

            // 切回来时从上次的位置继续（太靠近开头/结尾就不接，免得听着像卡了一下）
            float pos;
            if (_musicPos.TryGetValue(clip, out pos) && pos > 0.5f && pos < clip.length - 1.0f)
                _music.time = pos;
        }

        public void StopMusic() { if (_music != null) _music.Stop(); }
    }
}
