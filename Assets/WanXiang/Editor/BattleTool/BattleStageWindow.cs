// ============================================================================
//  万相 · 3D 灰盒战场窗口（菜单：万相/战斗/3D 灰盒战场）
//  ---------------------------------------------------------------------------
//  给美术方案 v1.1 的 **V1 形态**（纯色方块 + Tween + 伤害数字）配一个驾驶舱：
//  搭场景 / 播放 / 暂停 / 单步 / 变速 / 回开头 / 截图。
//
//  ⚠ 为什么用 EditorWindow 驱动而不是 Play 模式（与既有灰盒看板同一理由）：
//    本机编辑器不维持播放器循环（项目记忆），起 Play 看不了；而
//    `EditorApplication.update` 一直可靠。舞台本身是运行时组件，
//    进 Play 只是把 `SelfTick` 打开的事。
//
//  ⚠ 回放粒度：**逐事件**。ViewFrame 是增量帧（只装变了的单位），
//    "第 k 帧"要按 EventIndex 把 0..k 的差量叠起来；事件流负责伤害数字、
//    技能横幅、出手高亮这些"刚刚发生了什么"。
//    所以窗口同时持有两个游标：事件下标 + 已套用到的帧下标。
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
using WanXiang.Editor.WeatherTool;

namespace WanXiang.Editor.BattleTool
{
    public sealed class BattleStageWindow : EditorWindow
    {
        private BattleState _state;
        private BattleStageGraybox _stage;

        private int _eventIndex = -1;
        private int _frameIndex = -1;
        private bool _playing;
        private float _eventsPerSecond = 6f;
        private double _accum;
        private double _lastTick;

        private string _seedText = "20260914";
        private int _scenario;
        private int _weatherTerm;      // 0 = 无天时
        private bool _autoNext;

        private string _lastEventText = "（未开始）";
        private string _shotPath;

        [MenuItem("万相/战斗/3D 灰盒战场")]
        public static void Open()
        {
            var w = GetWindow<BattleStageWindow>("3D 灰盒战场");
            w.minSize = new Vector2(420f, 520f);
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        // ================================================================
        //  界面
        // ================================================================

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("样本", GUILayout.Width(32f));
                _scenario = EditorGUILayout.IntSlider(_scenario, 0, 6);
                GUILayout.Label("种子", GUILayout.Width(32f));
                _seedText = GUILayout.TextField(_seedText, GUILayout.Width(110f));
                if (GUILayout.Button("搭建场景", GUILayout.Width(90f))) BuildStage();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("天时", GUILayout.Width(32f));
                _weatherTerm = EditorGUILayout.IntSlider(_weatherTerm, 0, 24);
                GUILayout.Label(_weatherTerm == 0 ? "（无天时）"
                    : $"节气 {_weatherTerm}", GUILayout.Width(90f));
                _autoNext = GUILayout.Toggle(_autoNext, "跑完自动下一场", GUILayout.Width(130f));
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_playing ? "⏸ 暂停" : "▶ 播放", GUILayout.Width(80f)))
                    _playing = !_playing;
                if (GUILayout.Button("⏭ 单步", GUILayout.Width(70f))) { _playing = false; Advance(1); }
                if (GUILayout.Button("⏮ 回开头", GUILayout.Width(80f))) Rebuild(false);
                if (GUILayout.Button("⏩ 到结尾", GUILayout.Width(80f))) { _playing = false; JumpToEnd(); }
                if (GUILayout.Button("📷 截图", GUILayout.Width(80f))) Screenshot();
                GUILayout.FlexibleSpace();
                GUILayout.Label("速度", GUILayout.Width(32f));
                _eventsPerSecond = GUILayout.HorizontalSlider(_eventsPerSecond, 1f, 40f, GUILayout.Width(120f));
                GUILayout.Label($"{_eventsPerSecond:F0} 事件/秒", GUILayout.Width(80f));
            }

            EditorGUILayout.Space(6f);
            if (_state == null)
            {
                EditorGUILayout.HelpBox("点「搭建场景」：会在当前场景里用基础几何体搭出九宫格与 10 个单位，"
                    + "然后逐事件回放（含伤害数字、五行连线、技能横幅、出手高亮）。", MessageType.Info);
                return;
            }

            int total = _state.Log.Events.Count;
            GUILayout.Label($"进度　事件 {_eventIndex + 1}/{total}　帧 {_frameIndex + 1}/{_state.Frames.Count}"
                          + $"　回合 {CurrentTurn()}", EditorStyles.boldLabel);
            GUILayout.Label("当前事件：" + _lastEventText, EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(4f);
            GUILayout.Label("棋盘", EditorStyles.boldLabel);
            foreach (var line in BoardLines()) GUILayout.Label(line);

            if (!string.IsNullOrEmpty(_shotPath))
                GUILayout.Label("截图：" + _shotPath, EditorStyles.miniLabel);

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "灰盒看的是**事件密度与可读性**（美术方案 v1.1：伤害数字 / 相生相克连线 / "
                + "出手序列 / 状态 / 技能横幅 —— 这五个信号比方块动起来重要）。"
                + "连看 10 场觉得不腻 = V1 通过；觉得腻就回去调数值节奏，别先投美术。",
                MessageType.None);
        }

        // ================================================================
        //  搭建 / 回放
        // ================================================================

        private void BuildStage()
        {
            ulong seed = 20260914UL;
            ulong.TryParse(_seedText, out seed);

            _state = NewRun(seed);
            var go = GameObject.Find("~GrayboxStage");
            if (go == null)
            {
                go = new GameObject("~GrayboxStage");
                go.transform.position = Vector3.zero;
            }
            _stage = go.GetComponent<BattleStageGraybox>();
            if (_stage == null) _stage = go.AddComponent<BattleStageGraybox>();
            _stage.Build(_state);

            // 镜头对准舞台（Scene 视图里能直接看到）
            var scene = SceneView.lastActiveSceneView;
            if (scene != null)
            {
                scene.pivot = new Vector3(0f, 0.4f, 0f);
                scene.size = 6f;
                scene.Repaint();
            }
            Debug.Log($"[3D 灰盒] 场景已搭建：样本 {_scenario}、种子 {seed}、"
                    + $"天时 {(_weatherTerm == 0 ? "无" : _weatherTerm.ToString())}、"
                    + $"{_state.Log.Events.Count} 事件 / {_state.Frames.Count} 帧");
        }

        /// <summary>
        /// 装配并跑完一局，拿到完整事件流 + 帧流。
        /// ⚠ BuildScenario **没有天时参数**（它是 STEP 1 的脚手架），所以要挂天时得自己
        ///   走 BattleFactory：先按样本拿到两边的 DeployEntry，再用同种子重开一次带天时的。
        /// </summary>
        private BattleState NewRun(ulong seed)
        {
            var cfg = BattleConfig.Default;
            var probe = BattleSampleContent.BuildScenario(_scenario, seed, cfg);
            var player = new List<DeployEntry>();
            var enemy = new List<DeployEntry>();
            var units = probe.AllUnits;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var entry = new DeployEntry(u.Def, u.Side, u.Pos.Index);
                if (u.Side == TeamSide.Player) player.Add(entry); else enemy.Add(entry);
            }

            WeatherDef weather = _weatherTerm > 0 ? WeatherCatalog.GetSolarTerm(_weatherTerm) : null;
            var st = BattleFactory.Create(cfg, seed, player.ToArray(), enemy.ToArray(), weather);
            BattleSimulator.Run(st);

            _eventIndex = -1;
            _frameIndex = -1;
            _accum = 0;
            _lastTick = EditorApplication.timeSinceStartup;
            _lastEventText = "（已回到开头）";
            return st;
        }

        /// <summary>回开头：重跑一局并重建舞台（帧是增量流，倒着回放不值得，直接重建）。</summary>
        private void Rebuild(bool _)
        {
            ulong seed = 20260914UL;
            ulong.TryParse(_seedText, out seed);
            _state = NewRun(seed);
            if (_stage != null && _stage.gameObject != null) _stage.Build(_state);
        }

        private void JumpToEnd()
        {
            while (_eventIndex + 1 < _state.Log.Events.Count) Advance(1);
        }

        /// <summary>推进 n 条事件：先套用"已到达的帧"，再套用事件视觉。</summary>
        private void Advance(int n)
        {
            if (_state == null) return;
            int total = _state.Log.Events.Count;
            for (int k = 0; k < n && _eventIndex + 1 < total; k++)
            {
                _eventIndex++;
                var e = _state.Log.Events[_eventIndex];

                // 帧是"到这条事件为止"的累积状态：把 EventIndex <= 当前 的帧都套上
                while (_frameIndex + 1 < _state.Frames.Count
                       && _state.Frames[_frameIndex + 1].EventIndex <= _eventIndex)
                {
                    _frameIndex++;
                    _stage?.ApplyFrame(_state.Frames[_frameIndex]);
                }

                _stage?.ApplyEvent(_eventIndex, e);
                _lastEventText = Describe(e);
            }
        }

        private void OnEditorUpdate()
        {
            if (!_playing || _state == null) return;
            double now = EditorApplication.timeSinceStartup;
            double dt = now - _lastTick;
            _lastTick = now;
            if (dt < 0 || dt > 0.5) dt = 0;         // 窗口失焦/编辑器卡顿时不跳一大段

            _accum += dt;
            double interval = 1.0 / Mathf.Max(1f, _eventsPerSecond);
            int guard = 0;
            while (_accum >= interval && guard++ < 200)
            {
                _accum -= interval;
                if (_eventIndex + 1 >= _state.Log.Events.Count) break;
                Advance(1);
            }

            // 舞台动效由窗口推进（舞台不自己 Update —— 编辑器里没有稳定帧循环）
            _stage?.Step((float)dt);
            SceneView.RepaintAll();
            Repaint();

            if (_eventIndex + 1 >= _state.Log.Events.Count && _playing)
            {
                _playing = false;
                if (_autoNext)
                {
                    _scenario = (_scenario + 1) % 7;
                    BuildStage();
                    _playing = true;
                }
            }
        }

        // ================================================================
        //  截图（给复盘/汇报用：把灰盒画出来，比文字描述清楚）
        // ================================================================

        private void Screenshot()
        {
            if (_stage == null || _stage.Camera == null) { Debug.LogWarning("[3D 灰盒] 先搭建场景"); return; }
            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24);
            var cam = _stage.Camera;
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prevTarget;

            string path = $"Temp/WanXiangDiag/graybox_evt{_eventIndex + 1}.png";
            System.IO.Directory.CreateDirectory("Temp/WanXiangDiag");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            _shotPath = path;
            Debug.Log($"[3D 灰盒] 截图已存：{path}");
        }

        // ================================================================
        //  文本
        // ================================================================

        /// <summary>状态后缀（复用 ViewSnapshot 的口径，别在窗口里重写一套）。</summary>
        private static string StatusSuffix(BattleUnit u)
        {
            string s = ViewSnapshot.DescribeStatuses(u);
            return string.IsNullOrEmpty(s) ? "" : "　" + s;
        }

        private int CurrentTurn() => _state != null && _frameIndex >= 0
            ? _state.Frames[_frameIndex].Turn : 0;

        private static string Describe(in BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.ActionBegin: return $"回合{e.Turn} 出手：{e.ActorId}";
                case BattleEventKind.SkillCast: return $"回合{e.Turn} 技能：{e.SkillName}（{e.Skill}）";
                case BattleEventKind.Damage: return $"回合{e.Turn} 伤害：{e.ActorId} → {e.TargetId} = {e.Amount}"
                                                  + (string.IsNullOrEmpty(e.Note) ? "" : $"｜{e.Note}");
                case BattleEventKind.Crit: return $"回合{e.Turn} 暴击！{e.TargetId}";
                case BattleEventKind.Death: return $"回合{e.Turn} 阵亡：{e.TargetId}";
                case BattleEventKind.Heal: return $"回合{e.Turn} 回复：{e.TargetId} +{e.Amount}";
                case BattleEventKind.Shield: return $"回合{e.Turn} 护盾：{e.TargetId} +{e.Amount}";
                case BattleEventKind.StatusApplied: return $"回合{e.Turn} 状态：{e.Note}";
                default: return $"回合{e.Turn} {e.Kind}" + (string.IsNullOrEmpty(e.Note) ? "" : $"：{e.Note}");
            }
        }

        /// <summary>棋盘文本（与 3D 视图互为印证：谁在哪格、血多少、什么状态）。</summary>
        private List<string> BoardLines()
        {
            var lines = new List<string>(8);
            if (_state == null) return lines;
            var units = _state.AllUnits;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                int hp = u.Hp, max = u.MaxHp;
                lines.Add($"{(u.Side == TeamSide.Player ? "我" : "敵")}{u.Pos.Index}　"
                        + $"{Cn.Of(u.Element)}{u.DisplayName}　"
                        + $"HP {hp}/{max}" + (u.Shield > 0 ? $"+盾{u.Shield}" : "")
                        + $"　速度{_state.EffectiveSpeed(u):F0}"
                        + (u.IsAlive ? "" : "　☠")
                        + StatusSuffix(u));
            }
            return lines;
        }
    }
}
