// ============================================================================
//  万相 · 2D 战场搭建器 + 控制窗口（菜单：万相/战斗/2D 战场搭建与回放）
//  ---------------------------------------------------------------------------
//  一键摆好**2D 正交视角**的整块战场（美术方案 V2 形态）：
//
//    · 场景 Battle2D：背景（春夏各一，切换显示）+ 双 3×3 棋盘 + 相机
//    · Canvas（Overlay 1920×1080）：天时条 / 回合数 / 出手序列 / 三枚绝技按钮 /
//      天时覆盖按钮 / 非战斗事件面板 —— **全部是"要按的点位"**：
//      命名规范、位置尺寸摆好，用户后续只换 Image 贴图即可
//    · 回放：播放/单步/变速/截图（与 3D 灰盒同契约）
//
//  ⚠ 场景保存为 Assets/Scenes/Battle2D.unity（专用场景，不碰 SampleScene）。
//  ⚠ 所有可替换贴图的组件命名都带 `Skin_` 前缀说明或独立命名，替换点位见文档。
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
using WanXiang.Editor.BattleTool;

namespace WanXiang.Editor.TrialsTool
{
    public sealed class Battle2DBuilderWindow : EditorWindow
    {
        private BattleState _state;
        private BattleStage2D _stage;
        private int _eventIndex = -1;
        private int _frameIndex = -1;
        private bool _playing;
        private float _eps = 8f;
        private double _accum, _lastTick;
        private string _seedText = "20260914";
        private string _lastEvent = "（未开始）";
        private Sprite _spring, _summer;
        private bool _useSummer;

        [MenuItem("万相/战斗/2D 战场搭建与回放")]
        public static void Open() => GetWindow<Battle2DBuilderWindow>("2D 战场");

        private void OnEnable() => EditorApplication.update += Tick;
        private void OnDisable() => EditorApplication.update -= Tick;

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _seedText = GUILayout.TextField(_seedText, GUILayout.Width(110f));
                if (GUILayout.Button("搭建战场场景", GUILayout.Width(120f))) Build();
                _useSummer = GUILayout.Toggle(_useSummer, "夏背景", GUILayout.Width(80f));
                if (GUILayout.Button("重搭（应用背景切换）", GUILayout.Width(150f))) Build();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_playing ? "⏸ 暂停" : "▶ 播放", GUILayout.Width(80f))) _playing = !_playing;
                if (GUILayout.Button("⏭ 单步", GUILayout.Width(70f))) { _playing = false; Advance(1); }
                if (GUILayout.Button("⏩ 到结尾", GUILayout.Width(80f))) { _playing = false; while (Advance(1)) { } }
                if (GUILayout.Button("📷 截图", GUILayout.Width(80f))) Screenshot();
                GUILayout.Label($"速度 {_eps:F0} 事件/秒", GUILayout.Width(120f));
                _eps = GUILayout.HorizontalSlider(_eps, 1f, 40f, GUILayout.Width(120f));
            }

            if (_state == null)
            {
                EditorGUILayout.HelpBox(
                    "点「搭建战场场景」：会创建并保存 Assets/Scenes/Battle2D.unity ——\n" +
                    "2D 正交相机 + 春/夏背景 + 双 3×3 棋盘 + 立绘单位 + UI 点位全摆好。\n" +
                    "以后你只换图：BG_Spring/BG_Summer、Cell_x、Unit_x 的 Sprite、\n" +
                    "Canvas 下 WeatherBar / Btn_Skill0..2 / Btn_Weather / Panel_NonBattle 的 Image。",
                    MessageType.Info);
                return;
            }
            GUILayout.Label($"进度 {_eventIndex + 1}/{_state.Log.Events.Count}｜{_lastEvent}", EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox(
                "替换素材点位（换图不改代码）：\n" +
                "· 背景：BG_Spring / BG_Summer（SpriteRenderer）\n" +
                "· 棋盘格：BoardP/Cell0..8、BoardE/Cell0..8\n" +
                "· 单位立绘：SpriteCatalog 资产里按 BeastDef.Id 换\n" +
                "· UI：Canvas/WeatherBar、TurnText、OrderStrip、Btn_Skill0..2、Btn_WeatherOverride、Panel_NonBattle",
                MessageType.None);
        }

        // ================================================================
        //  搭建
        // ================================================================

        private void Build()
        {
            ulong seed = 20260914UL;
            ulong.TryParse(_seedText, out seed);

            // 1) 新建专用场景（不碰 SampleScene）
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2) 装一场战斗
            var cfg = BattleConfig.Default;
            cfg.LegendMultiplier = 1.90f;   // 演示用"毕业队"（§5.7 曲线，见对齐清单）
            _state = BattleSampleContent.BuildScenario(5, seed, cfg);
            BattleSimulator.Run(_state);

            // 3) 背景
            _spring = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/WanXiang/Art/ArtRes/Battle/BG_spring.png");
            _summer = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/WanXiang/Art/ArtRes/Battle/BG_summer.png");

            // 4) 舞台（2D）
            var root = new GameObject("~Battle2D");
            _stage = root.AddComponent<BattleStage2D>();
            _stage.BgSpring = _useSummer ? _summer : _spring;
            _stage.Build(_state, _useSummer ? _summer : _spring);

            // 5) UI 点位
            BuildUICanvas(root.transform);

            // 6) 存场景
            var dir = "Assets/Scenes";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Battle2D.unity");

            _eventIndex = -1; _frameIndex = -1;
            _lastEvent = "已搭建并保存 Battle2D.unity";
            Debug.Log("[2D 战场] 搭建完成：Battle2D.unity（相机 ortho 5.4、UI 1920×1080 点位全摆好）");
        }

        private void BuildUICanvas(Transform parent)
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);

            // —— 顶部：天时条（左上）——
            var weather = NewImage(canvas.transform, "WeatherBar", new Color(0.96f, 0.94f, 0.86f, 0.92f),
                                   new Vector2(430, 64), new Vector2(250, 40));
            NewText(weather.transform, "WeatherText", "天时：立春 · 东风解冻", 26,
                    new Color(0.16f, 0.13f, 0.09f), TextAnchor.MiddleLeft);

            // —— 顶部：回合数（顶中）——
            var turn = NewText(canvas.transform, "TurnText", "第 1 回合", 34,
                               new Color(0.16f, 0.13f, 0.09f), TextAnchor.MiddleCenter);
            var trt = turn.rectTransform;
            trt.sizeDelta = new Vector2(300, 50);
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0, -34);

            // —— 右缘：出手序列条 ——
            var strip = NewImage(canvas.transform, "OrderStrip", new Color(0.96f, 0.94f, 0.86f, 0.85f),
                                 new Vector2(84, 620), new Vector2(1840, 0));
            for (int i = 0; i < 10; i++)
            {
                var slot = NewImage(strip.transform, $"Slot{i}",
                                    i == 0 ? new Color(0.79f, 0.63f, 0.39f) : new Color(0.85f, 0.82f, 0.74f),
                                    new Vector2(64, 48), new Vector2(0, -60 * i));
                NewText(slot.transform, "T", $"{i + 1}", 22, Color.white, TextAnchor.MiddleCenter);
            }

            // —— 底中：三枚技能/绝技按钮（"要按的点位"）——
            string[] labels = { "绝技 · 甲", "绝技 · 乙", "天时覆盖" };
            for (int i = 0; i < 3; i++)
            {
                var btn = NewButton(canvas.transform, $"Btn_Skill{i}", labels[i],
                                    new Color(0.16f, 0.13f, 0.09f, 0.9f),
                                    new Vector2(210, 150), new Vector2((i - 1) * 240, 110));
                NewText(btn.transform, "CD", "CD 3", 22, new Color(0.96f, 0.94f, 0.86f),
                        TextAnchor.LowerRight);
            }

            // —— 右下：天时覆盖（逆天改势）按钮 ——
            NewButton(canvas.transform, "Btn_WeatherOverride", "逆天改势",
                      new Color(0.20f, 0.30f, 0.42f, 0.95f),
                      new Vector2(260, 84), new Vector2(780, 110));

            // —— 居中：非战斗事件面板（灵市/孵穴/天象/异闻共用模板，默认隐藏）——
            var panel = NewImage(canvas.transform, "Panel_NonBattle", new Color(0.96f, 0.94f, 0.86f, 0.97f),
                                 new Vector2(760, 460), new Vector2(0, 0));
            panel.gameObject.SetActive(false);
            NewText(panel.transform, "Title", "灵市 · 玄鸟至", 34,
                    new Color(0.16f, 0.13f, 0.09f), TextAnchor.UpperCenter);
            for (int i = 0; i < 3; i++)
                NewButton(panel.transform, $"Option{i + 1}", $"选项 {i + 1}",
                          new Color(0.85f, 0.82f, 0.74f),
                          new Vector2(640, 90), new Vector2(0, -60 - i * 110));
            NewButton(panel.transform, "Skip", "拒绝（拿 1 灵卵）",
                      new Color(0.45f, 0.41f, 0.35f),
                      new Vector2(640, 70), new Vector2(0, -190));
        }

        // ================================================================
        //  UI 小工厂
        // ================================================================

        private static Image NewImage(Transform parent, string name, Color c,
                                      Vector2 size, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            go.GetComponent<Image>().color = c;
            return go.GetComponent<Image>();
        }

        private static GameObject NewButton(Transform parent, string name, string label,
                                            Color c, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = c;
            go.GetComponent<Button>().onClick.AddListener(() =>
                Debug.Log($"[UI 点位] {name} 被点击 —— 交互逻辑接这里"));

            var tgo = new GameObject("Label", typeof(Text));
            tgo.transform.SetParent(go.transform, false);
            var rt2 = tgo.GetComponent<RectTransform>();
            rt2.sizeDelta = size;
            rt2.localPosition = Vector3.zero;
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var text = tgo.GetComponent<Text>();
            if (f != null) text.font = f;
            text.text = label;
            text.fontSize = 30;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.96f, 0.94f, 0.86f);
            return go;
        }

        private static Text NewText(Transform parent, string name, string content, float size,
                                    Color c, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(420, 50);
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var text = go.GetComponent<Text>();
            if (f != null) text.font = f;
            text.text = content;
            text.fontSize = (int)size;
            text.color = c;
            text.alignment = anchor;
            return text;
        }

        // ================================================================
        //  回放
        // ================================================================

        private bool Advance(int n)
        {
            if (_state == null) return false;
            bool any = false;
            int total = _state.Log.Events.Count;
            for (int k = 0; k < n && _eventIndex + 1 < total; k++)
            {
                _eventIndex++;
                any = true;
                var e = _state.Log.Events[_eventIndex];
                while (_frameIndex + 1 < _state.Frames.Count
                       && _state.Frames[_frameIndex + 1].EventIndex <= _eventIndex)
                {
                    _frameIndex++;
                    if (_stage != null) _stage.ApplyFrame(_state.Frames[_frameIndex]);
                }
                _stage?.ApplyEvent(_eventIndex, e);
                _lastEvent = $"{e.Kind} {(e.Note ?? "")}";
            }
            return any;
        }

        private void Tick()
        {
            if (!_playing || _state == null) return;
            double now = EditorApplication.timeSinceStartup;
            double dt = now - _lastTick;
            _lastTick = now;
            if (dt < 0 || dt > 0.5) dt = 0;
            _accum += dt;
            double interval = 1.0 / Mathf.Max(1f, _eps);
            int guard = 0;
            while (_accum >= interval && guard++ < 200)
            {
                _accum -= interval;
                if (_eventIndex + 1 >= _state.Log.Events.Count) { _playing = false; break; }
                Advance(1);
            }
            _stage?.Step((float)dt);
            SceneView.RepaintAll();
            Repaint();
        }

        private void Screenshot()
        {
            if (_stage?.Camera == null) { Debug.LogWarning("[2D 战场] 先搭建"); return; }
            const int w = 1920, h = 1080;
            var rt = new RenderTexture(w, h, 24);
            var cam = _stage.Camera;
            cam.targetTexture = rt;
            cam.Render();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            string path = $"Temp/WanXiangDiag/battle2d_evt{_eventIndex + 1}.png";
            System.IO.Directory.CreateDirectory("Temp/WanXiangDiag");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[2D 战场] 截图：{path}");
        }
    }
}
