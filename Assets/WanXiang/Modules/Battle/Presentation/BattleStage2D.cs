// ============================================================================
//  万相 · 2D 战场（美术方案 V2 形态：立绘 + Sprite 全套）
//  ---------------------------------------------------------------------------
//  2D 正交视角。与 3D 灰盒（BattleStageGraybox）共用同一套数据契约
//  （只读 ViewFrame 快照 + BattleEvent 事件流），差别只在"画的东西"：
//      · 背景一张季别远景（ArtRes/Battle/BG_xxx.png）
//      · 每方一块 3×3 棋盘（格子 Sprite，可整体换图）
//      · 单位 = 立绘 SpriteRenderer（SpriteCatalog 按 BeastDef.Id 查，
//        查不到回退五行色块 —— 新异兽没图也不会穿帮）
//      · 血条 = 双 Sprite、伤害数字 = TextMesh 上浮、死亡 = 灰化+下沉
//
//  ⚠ 用户替换素材的点位（全部命名规范，换 Sprite/Image 即可，不动代码）：
//      BG_Spring / BG_Summer        背景
//      BoardP/Cell0..8、BoardE/...  棋盘格（含中宫）
//      Unit_xxx/Body                单位立绘
//      Canvas/WeatherBar、Btn_*     UI 贴图
// ============================================================================
//
//  ⚠ 相机与布局（2D 正交）：
//    相机 orthographicSize 5.4、位于 (0,0,-10)，宣纸色清屏；
//    我方棋盘中心 (-2.95, -0.2)、敌方 (2.95, -0.2)，格距 1.15，单位高 ~1.7；
//    UI 用 Overlay Canvas 1920×1080（见 Battle2DSceneBuilder）。

using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    internal sealed class UnitView2D
    {
        public string UnitId;
        public GameObject Root;        // 立绘容器
        public SpriteRenderer Body;    // 立绘
        public SpriteRenderer HpBg;
        public SpriteRenderer HpFill;
        public TextMesh NameText;
        public int Hp, MaxHp;
        public bool Alive = true;
        public Vector2 HomePos;        // 站位（未加浮动）
        public float Phase;            // 待机浮动相位（随机，方案 §最易做错三件事之一）
        public float DeadBlend;
        public float ShakePulse;         // 受击回弹（>0 时向基色回退）
        public Color BaseColor = Color.white;
    }

    public sealed class BattleStage2D : MonoBehaviour
    {
        private const float Cell = 1.15f;
        private const float PlayerX = -2.95f;
        private const float EnemyX = 2.95f;

        public Sprite BgSpring;        // ArtRes/Battle/BG_spring.png
        public Sprite CatalogSpriteFallback;

        private BattleState _state;
        private readonly Dictionary<string, UnitView2D> _views = new Dictionary<string, UnitView2D>(16);
        private readonly List<GameObject> _tempTexts = new List<GameObject>(16);
        private readonly List<float> _tempLife = new List<float>(16);
        private Camera _cam;
        private string _actingId;

        public Camera Camera => _cam;

        // ================================================================
        //  搭建
        // ================================================================

        public void Build(BattleState st, Sprite springBg)
        {
            Clear();
            _state = st;

            var bg = new GameObject("BG_Spring");
            bg.transform.SetParent(transform, false);
            var bgSr = bg.AddComponent<SpriteRenderer>();
            if (springBg != null)
            {
                bgSr.sprite = springBg;
                // 铺满相机视野（ortho 5.4 → 高 10.8、宽 19.2 @16:9）
                var b = springBg.bounds;
                float sx = 19.2f / b.size.x, sy = 10.8f / b.size.y;
                bg.transform.localScale = new Vector3(sx, sy, 1f);
            }
            bgSr.sortingOrder = -10;

            BuildBoard("BoardP", PlayerX, -0.2f);
            BuildBoard("BoardE", EnemyX, -0.2f);
            BuildUnits();
            BuildCamera();
        }

        private void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            _views.Clear();
            _tempTexts.Clear();
            _tempLife.Clear();
            _state = null;
            _cam = null;
        }

        private void BuildBoard(string boardName, float cx, float cy)
        {
            var board = new GameObject(boardName);
            board.transform.SetParent(transform, false);
            for (int i = 0; i < 9; i++)
            {
                bool center = i == 4;
                var cell = new GameObject($"Cell_{boardName[5]}{i}" + (center ? "_center" : ""));
                cell.transform.SetParent(board.transform, false);
                var sr = cell.AddComponent<SpriteRenderer>();
                // 格子：程序化纯色 + 墨色描边（用户换图点位：Cell_x 的 sprite 直接换）
                sr.sprite = SolidSprite(
                    center ? new Color(0.94f, 0.89f, 0.78f) : new Color(0.91f, 0.89f, 0.82f),
                    (int)(Cell * 100), (int)(Cell * 100), 3,
                    center ? new Color(0.79f, 0.63f, 0.39f) : new Color(0.16f, 0.13f, 0.09f));
                sr.sortingOrder = -5;
                int col = i % 3, row = i / 3;
                cell.transform.localPosition = new Vector3(
                    cx + (col - 1) * Cell, cy + (1 - row) * Cell * 0.72f, 0f);
            }
        }

        private void BuildUnits()
        {
            var catalog = FindCatalog();
            var units = _state.AllUnits;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.Pos.IsValid) continue;

                var view = new UnitView2D
                {
                    UnitId = u.RuntimeId,
                    Hp = u.Hp, MaxHp = u.MaxHp,
                    Alive = u.IsAlive,
                };

                var root = new GameObject($"Unit_{u.RuntimeId}");
                root.transform.SetParent(transform, false);
                bool player = u.Side == TeamSide.Player;
                view.HomePos = CellPos(player, u.Pos.Index);
                root.transform.localPosition = new Vector3(view.HomePos.x, view.HomePos.y, 0f);
                view.Root = root;

                // 立绘：SpriteCatalog 按 BeastDef.Id 查；查不到回退五行色块
                var bodySr = root.AddComponent<SpriteRenderer>();
                var sprite = catalog != null ? catalog.Get(u.Def.Id) : null;
                // ⚠ 测试阵容 Id（pw/eg 等）对不上 catalog 里的真实异兽 id ——
                //   立绘查不到就走色块 fallback。等真实内容接入后 Id 会对上。
                if (sprite != null)
                {
                    bodySr.sprite = sprite;
                    // 显示高 ~1.7 世界单位：按 sprite 尺寸等比缩放
                    float h = sprite.bounds.size.y;
                    if (h > 0.001f)
                    {
                        float k = 1.7f / h;
                        root.transform.localScale = new Vector3(k, k, 1f);
                    }
                }
                else
                {
                    // fallback 色块：96px PPU=100 = 0.96 世界单位，直接可用
                    bodySr.sprite = SolidSprite(BattlePalette.OfElement(u.Element), 96, 128, 3,
                                                new Color(0.16f, 0.13f, 0.09f));
                }
                bodySr.sortingOrder = 0;
                // 敌方压暗一档（§4.4 敌我同源 + 浊化的轻量版）
                if (!player) bodySr.color = new Color(0.72f, 0.72f, 0.80f);
                view.Body = bodySr;
                view.BaseColor = bodySr.color;

                view.HpBg = MakeChildSprite(root, "HpBg", new Color(0.10f, 0.08f, 0.06f),
                                            new Vector2(0.95f, 0.11f), new Vector2(0f, 0.95f), 2);
                view.HpFill = MakeChildSprite(root, "HpFill", BattlePalette.Vital,
                                              new Vector2(0.90f, 0.075f), new Vector2(0f, 0.95f), 3);
                SetHpBar2D(view, u.Hp, u.MaxHp);

                var nameGo = new GameObject("Name");
                nameGo.transform.SetParent(root.transform, false);
                nameGo.transform.localPosition = new Vector3(0f, 1.12f, 0f);
                var f = LegacyFont();
                view.NameText = nameGo.AddComponent<TextMesh>();
                if (f != null)
                {
                    view.NameText.font = f;
                    nameGo.GetComponent<MeshRenderer>().sharedMaterial = f.material;
                }
                view.NameText.text = u.DisplayName;
                view.NameText.characterSize = 0.055f;
                view.NameText.fontSize = 48;
                view.NameText.anchor = TextAnchor.LowerCenter;
                view.NameText.alignment = TextAlignment.Center;
                view.NameText.color = new Color(0.16f, 0.13f, 0.09f);

                int h2 = u.RuntimeId != null ? u.RuntimeId.GetHashCode() : i * 7919;
                view.Phase = (Mathf.Abs(h2) % 1000) / 1000f * Mathf.PI * 2f;

                _views[u.RuntimeId] = view;
            }
        }

        private void BuildCamera()
        {
            var go = new GameObject("Cam2D");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 5.4f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = BattlePalette.Paper;
            go.transform.localPosition = new Vector3(0f, 0f, -10f);
        }

        // ================================================================
        //  回放（与 3D 灰盒同契约：ApplyFrame + ApplyEvent + Step）
        // ================================================================

        public void ApplyFrame(ViewFrame frame)
        {
            if (frame.Changed == null) return;
            for (int i = 0; i < frame.Changed.Length; i++)
            {
                var s = frame.Changed[i];
                if (!_views.TryGetValue(s.UnitId, out var v)) continue;
                v.Hp = s.Hp; v.MaxHp = s.MaxHp;
                SetHpBar2D(v, s.Hp, s.MaxHp);
                if (v.Alive && !s.Alive) v.DeadBlend = 0f;
                v.Alive = s.Alive;
            }
        }

        public void ApplyEvent(int index, BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.ActionBegin:
                    _actingId = e.ActorId;
                    break;

                case BattleEventKind.Damage:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var hit))
                    {
                        SpawnDamageNumber(hit, e.Amount, e.Note != null && e.Note.Contains("融冰"));
                        hit.Body.color = new Color(1f, 0.55f, 0.5f);
                        hit.ShakePulse = 0.16f;
                    }
                    break;

                case BattleEventKind.Crit:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var cv))
                        cv.Body.color = new Color(1f, 0.25f, 0.2f);
                    break;

                case BattleEventKind.Death:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var dv))
                        dv.Alive = false;
                    break;
            }
        }

        public void Step(float dt)
        {
            foreach (var v in _views.Values)
            {
                if (v.Root == null) continue;
                float bob = Mathf.Sin((Time.realtimeSinceStartup + v.Phase) * 2.6f) * 0.035f;
                float sink = 0f;
                if (!v.Alive)
                {
                    v.DeadBlend = Mathf.Min(1f, v.DeadBlend + dt / 0.35f);
                    sink = -0.5f * v.DeadBlend;
                    var c = v.Body.color;
                    float g = Mathf.Lerp(1f, 0.35f, v.DeadBlend);
                    v.Body.color = new Color(v.BaseColor.r * g, v.BaseColor.g * g, v.BaseColor.b * g, 1f - 0.5f * v.DeadBlend);
                }
                else if (v.Body.color != v.BaseColor)
                {
                    v.Body.color = Color.Lerp(v.Body.color, v.BaseColor, dt * 10f);
                }
                v.Root.transform.localPosition = new Vector3(v.HomePos.x, v.HomePos.y + bob + sink, 0f);

                // 血条跟随立绘（相机 2D 朝 -Z，直接摆即可）
                if (v.HpBg != null) v.HpBg.transform.localPosition = new Vector3(0f, 0.95f, -0.01f);
                if (v.HpFill != null) v.HpFill.transform.localPosition = new Vector3(0f, 0.95f, -0.02f);
            }

            // 伤害数字上浮 + 回收
            for (int i = _tempTexts.Count - 1; i >= 0; i--)
            {
                _tempLife[i] -= dt;
                var go = _tempTexts[i];
                if (go != null)
                {
                    go.transform.localPosition += new Vector3(0f, dt * 0.9f, 0f);
                    var tm = go.GetComponent<TextMesh>();
                    if (tm != null)
                    {
                        var c = tm.color;
                        tm.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(_tempLife[i] / 0.7f));
                    }
                }
                if (_tempLife[i] <= 0f)
                {
                    if (go != null) DestroyImmediate(go);
                    _tempTexts.RemoveAt(i); _tempLife.RemoveAt(i);
                }
            }
        }

        // ================================================================

        private void SpawnDamageNumber(UnitView2D v, int amount, bool isMelt)
        {
            var go = new GameObject("Dmg");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = v.Root.transform.position + new Vector3(0f, 0.9f, -1f);
            var f = LegacyFont();
            var tm = go.AddComponent<TextMesh>();
            if (f != null)
            {
                tm.font = f;
                go.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            tm.text = amount.ToString();
            tm.characterSize = 0.09f;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = isMelt ? BattlePalette.GoldRich : BattlePalette.Crimson;
            _tempTexts.Add(go);
            _tempLife.Add(0.7f);
            while (_tempTexts.Count > 8)
            {
                if (_tempTexts[0] != null) DestroyImmediate(_tempTexts[0]);
                _tempTexts.RemoveAt(0); _tempLife.RemoveAt(0);
            }
        }

        private static void SetHpBar2D(UnitView2D v, int hp, int max)
        {
            if (v.HpFill == null || max <= 0) return;
            float ratio = Mathf.Clamp01((float)hp / max);
            var t = v.HpFill.transform;
            var s = t.localScale;
            t.localScale = new Vector3(0.90f * ratio, s.y, s.z);
            t.localPosition = new Vector3(-(0.90f - 0.90f * ratio) * 0.5f, t.localPosition.y, t.localPosition.z);
            v.HpFill.color = ratio > 0.5f ? BattlePalette.Vital
                          : ratio > 0.25f ? BattlePalette.Gold : BattlePalette.Crimson;
        }

        // ================================================================
        //  工具
        // ================================================================

        private static Vector2 CellPos(bool player, int cell)
        {
            int col = cell % 3, row = cell / 3;
            float cx = player ? PlayerX : EnemyX;
            // 我方在后（row 0 靠外），敌方镜像；行距压一点制造纵深
            float dy = (1 - row) * Cell * 0.66f;
            float y = (player ? -0.55f : 0.55f) + dy * (player ? 1f : -1f);
            return new Vector2(cx + (col - 1) * Cell * 0.86f, y);
        }

        private SpriteRenderer MakeChildSprite(GameObject parent, string name, Color c,
                                               Vector2 size, Vector2 localPos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, -0.01f * order);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SolidSprite(c, (int)(size.x * 100), (int)(size.y * 100), 0, c);
            sr.sortingOrder = order;
            return sr;
        }

        private static Sprite _solid;
        public static Sprite SolidSprite(Color c, int w, int h, int border, Color borderColor)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool edge = border > 0 && (x < border || y < border || x >= w - border || y >= h - border);
                    px[y * w + x] = edge ? borderColor : c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Font _legacyFont;
        private static Font LegacyFont()
        {
            if (_legacyFont != null) return _legacyFont;
            _legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_legacyFont == null) _legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _legacyFont;
        }

        private SpriteCatalog _catalog;
        private SpriteCatalog FindCatalog()
        {
            if (_catalog != null) return _catalog;
            // Resources.FindObjectsOfTypeAll 在编辑器里能找到**未加载进场景的资产**（含 SO）
            var all = Resources.FindObjectsOfTypeAll<SpriteCatalog>();
            foreach (var c in all)
                if (c != null && c.Entries.Count > 0) { _catalog = c; break; }
            return _catalog;
        }
    }
}
