// ============================================================================
//  BossRushPanel —— 首领试炼（方案 B，2026-10-25）
//  ---------------------------------------------------------------------------
//  用户："主界面做一个 Boss 连战，包括秘境的 Boss，方便测技能；队伍用分享码那套养成"
//  本面板：左侧一列全部首领（按幕/场域分组），点一个直接开战；打完回列表（不连播）。
//
//  ⚠ 阶段说明
//   · 主线 14 个：`BossCatalog.BuildBoss(id)` 直接成战；
//   · 秘境 10 个：战斗体由 `RealmMapBuilder` 按场域+钩子现建，本版先列出、不可开战；
//   · 我方队伍：优先用当前旅程的**队伍**（含养成），没有则用 Collection 里战力最高的 5 只。
// ============================================================================
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    public sealed class BossRushPanel : UIPanelBase
    {
        [Header("引用（prefab 里绑定）")]
        [SerializeField] private TMP_Text _tmpTitle;
        [SerializeField] private TMP_Text _tmpHint;
        [SerializeField] private TMP_Text _tmpTeam;
        [SerializeField] private RectTransform _rootList;
        [SerializeField] private RectTransform _itemTemplate;   // 一行一条（模板，默认隐藏）
        [SerializeField] private Button _btnClose;
        [SerializeField] private SpriteCatalog _sprites;
        [SerializeField] private ContentCatalogSO _content;
        [SerializeField] private TMP_InputField _inputCode;   // 对战码输入（可空）
        [SerializeField] private Button _btnLoadCode;         // 载入对战码

        [Header("右侧详情（点横条查看）")]
        [SerializeField] private TMP_Text _tmpDName;          // 名字 + 称号
        [SerializeField] private TMP_Text _tmpDStats;         // HP / ATK / DEF / SPD
        [SerializeField] private TMP_Text _tmpDSkills;        // 三个技能
        [SerializeField] private TMP_Text _tmpDMech;          // 机制说明

        private readonly List<GameObject> _rows = new List<GameObject>(32);

        protected override void OnCreate() { }

        protected override UniTask OnOpenAsync(object payload)
        {
            if (_itemTemplate != null) _itemTemplate.gameObject.SetActive(false);
            if (_btnClose != null) _btnClose.onClick.AddListener(CloseSelf);
            if (_tmpTitle != null) _tmpTitle.text = "首领试炼";
            BuildList();
            if (_btnLoadCode != null) _btnLoadCode.onClick.AddListener(OnLoadCode);
            RefreshTeamLabel();
            if (_tmpHint != null) _tmpHint.text = "选一个首领开战；打完整场会回到这里（方便逐个测技能）。";
            return UniTask.CompletedTask;
        }

        protected override void OnClose()
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i] != null) Destroy(_rows[i]);
            _rows.Clear();
        }

        // ------------------------------------------------------------------
        //  列表
        // ------------------------------------------------------------------
        private void BuildList()
        {
            if (_rootList == null || _itemTemplate == null) return;

            var all = BossRushCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];

                // 分组标题：第一行或分组变化时，单独插一行（不塞进名字里，避免溢出压行）
                string group = BossRushCatalog.GroupTitle(e);
                bool newGroup = i == 0 || BossRushCatalog.GroupTitle(all[i - 1]) != group;
                if (newGroup) MakeGroupRow(group);

                var row = Instantiate(_itemTemplate, _rootList);
                row.gameObject.SetActive(true);
                row.name = "Item_" + (string.IsNullOrEmpty(e.Id) ? e.RealmId + "_" + i : e.Id);
                _rows.Add(row.gameObject);

                var txts = row.GetComponentsInChildren<TMP_Text>(true);
                // 约定：0 = 名字（大），1 = 标签（小）
                if (txts.Length > 0) txts[0].text = e.Name;
                if (txts.Length > 1)
                    txts[1].text = e.Playable ? ElementLabel(e.Element) : "接入中";

                // 行底板：统一 panel_strip（主线/秘境都要有框）
                var rowImg = row.GetComponent<Image>();
                if (rowImg != null)
                {
                    var strip = Resources.Load<Sprite>("UI/Kit/panel_strip");
                    if (strip != null) { rowImg.sprite = strip; rowImg.type = Image.Type.Sliced; rowImg.color = Color.white; }
                }

                // 头像：按节点名取（不是"第一个 Image"，那是行底板）
                var headT = row.Find("Img_Head");
                var headImg = headT != null ? headT.GetComponent<Image>() : null;
                if (headImg != null)
                {
                    Sprite sp = null;
                    if (!string.IsNullOrEmpty(e.RealmId) && !string.IsNullOrEmpty(e.ChampionName))
                        sp = Resources.Load<Sprite>("UI/Realm/Champion/" + e.ChampionName);
                    if (sp == null && _sprites != null && !string.IsNullOrEmpty(e.Id)) sp = _sprites.GetHead(e.Id);
                    if (sp == null && _sprites != null && !string.IsNullOrEmpty(e.Id)) sp = _sprites.Get(e.Id);
                    if (sp != null)
                    {
                        headImg.sprite = sp;
                        headImg.type = Image.Type.Simple;
                        headImg.preserveAspect = true;
                        headImg.enabled = true;
                    }
                    else
                    {
                        // 实在没有图：至少画一个元素色块，别留空洞
                        headImg.sprite = null;
                        headImg.color = new Color(0.45f, 0.38f, 0.28f, 0.55f);
                    }
                }

                var captured = e;
                // 行内「挑战」按钮（用户要求：不要整行点就进战斗）
                Transform challengeT = null;
                foreach (var t in row.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Btn_Challenge") { challengeT = t; break; }
                if (challengeT != null)
                {
                    var cb = challengeT.GetComponent<Button>();
                    if (cb != null) { cb.onClick.RemoveAllListeners(); cb.onClick.AddListener(() => OnPick(captured)); }
                    var rowBtn = row.GetComponent<Button>();
                    if (rowBtn != null) rowBtn.onClick.RemoveAllListeners();   // 整行不再触发
                }
                else
                {
                    var btn = row.GetComponent<Button>();
                    if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => OnPick(captured)); }
                }

                // ★ 2026-10-26 用户："点左边横条，右边看这个 Boss 的详细信息"
                //   整行点击 = 选中并展示详情；开战仍只走「挑战」按钮。
                var rowBtn2 = row.GetComponent<Button>();
                if (rowBtn2 != null && challengeT != null)
                {
                    rowBtn2.onClick.RemoveAllListeners();
                    rowBtn2.onClick.AddListener(() => ShowDetail(captured));
                }
            }
        }

        /// <summary>组标题行：复用行模板，去掉头像与挑战按钮，只留一行小字。</summary>
        private void MakeGroupRow(string title)
        {
            if (_itemTemplate == null) return;
            var row = Instantiate(_itemTemplate, _rootList);
            row.gameObject.SetActive(true);
            row.name = "Group_" + title;
            _rows.Add(row.gameObject);

            var bg = row.GetComponent<Image>();
            if (bg != null) bg.color = new Color(1f, 1f, 1f, 0.35f);
            var head = row.Find("Img_Head");
            if (head != null) head.gameObject.SetActive(false);
            var ch = row.Find("Btn_Challenge");
            if (ch != null) ch.gameObject.SetActive(false);

            var txts = row.GetComponentsInChildren<TMP_Text>(true);
            if (txts.Length > 0)
            {
                txts[0].text = "<size=64%><color=#7A6B55>" + title + "</color></size>";
                txts[0].alignment = TextAlignmentOptions.Left;
            }
            if (txts.Length > 1) txts[1].text = "";
            var b = row.GetComponent<Button>();
            if (b != null) b.onClick.RemoveAllListeners();
        }

        private static string ElementLabel(Element el)
        {
            switch (el)
            {
                case Element.Wood: return "木";
                case Element.Fire: return "火";
                case Element.Metal: return "金";
                case Element.Water: return "水";
                case Element.Earth: return "土";
                default: return "无";
            }
        }

        // ------------------------------------------------------------------
        //  开战
        // ------------------------------------------------------------------
        /// <summary>点横条：右侧展示这个首领的技能/数值/机制。</summary>
        private void ShowDetail(BossRushEntry e)
        {
            BossDef def = null;
            if (!string.IsNullOrEmpty(e.RealmId))
            {
                var bodyId = ElementBodyBossId(e.Element);
                if (!string.IsNullOrEmpty(bodyId)) def = BossCatalog.Get(bodyId);
            }
            else def = BossCatalog.Get(e.Id);

            if (def == null) return;

            var sk = BossSkills.Default(def.Element, def.FlavorStatus);
            string title = e.Name;
            if (!string.IsNullOrEmpty(e.RealmId))
                title = e.ChampionName + "  ·  " + e.RealmName;

            if (_tmpDName != null)
            {
                _tmpDName.text = title + (string.IsNullOrEmpty(e.Epithet) ? "" : "\n<size=70%><color=#8A7A5F>" + e.Epithet + "</color></size>");
            }
            if (_tmpDStats != null)
            {
                _tmpDStats.text = "HP " + def.BaseHp + "    ATK " + def.BaseAtk +
                                  "    DEF " + def.BaseDef + "    SPD " + def.BaseSpeed +
                                  "\n<size=75%><color=#7A6B55>元素 " + ElementLabel(def.Element) +
                                  "    暴击 " + (def.CritRate * 100f).ToString("0") + "%" +
                                  "    暴伤 +" + (def.CritDamage * 100f).ToString("0") + "%</color></size>";
            }
            if (_tmpDSkills != null)
            {
                _tmpDSkills.text = "普攻  " + (sk[0] != null ? sk[0].Name : "-") +
                                   "\n战技  " + (sk[1] != null ? sk[1].Name : "-") +
                                   "\n绝技  " + (sk[2] != null ? sk[2].Name : "-");
            }
            if (_tmpDMech != null)
            {
                var mech = string.IsNullOrEmpty(e.Mechanic) ? BossCatalog.MechanicText(e.Id) : e.Mechanic;
                _tmpDMech.text = string.IsNullOrEmpty(mech) ? "机制：无（普通首领）" : mech;
            }
            if (_tmpHint != null) _tmpHint.text = "已选中「" + title + "」，点右侧「挑战」开战。";
        }

        /// <summary>秘境镇守借用的战斗体：取该属性的主线首领 id。</summary>
        private static string ElementBodyBossId(Element el)
        {
            foreach (var d in BossCatalog.All)
            {
                if (d == null || string.IsNullOrEmpty(d.Id) || !d.Id.StartsWith("b_")) continue;
                if (d.Element == el) return d.Id;
            }
            return BossCatalog.All.Count > 0 ? BossCatalog.All[0].Id : null;
        }

        private void OnPick(BossRushEntry e)
        {
            if (!e.Playable)
            {
                if (_tmpHint != null)
                    _tmpHint.text = "「" + e.Name + "」的战斗体还在接入中（秘境首领按场域+钩子现建，下一阶段开放）。";
                return;
            }

            var req = BuildRequest(e);
            if (req == null)
            {
                if (_tmpHint != null) _tmpHint.text = "拿不到战斗数据（内容目录或队伍为空）。";
                return;
            }

            // ★ 场景按 Boss 归属：主线用所在幕，秘境用场域（否则全是第一幕春景）
            SceneFlow.LastWasBossRush = true;
            SceneFlow.PendingBattleAct = e.Act;
            SceneFlow.PendingBattleRealmId = e.RealmId;

            CloseSelf();
            SceneFlow.EnterBattle(req);
        }

        private BattleRequest BuildRequest(BossRushEntry e)
        {
            // 秘境首领：没有独立 BeastDef ⇒ 取**同属性**的主线 Boss 作战斗体，
            // 再把场域镇守钩子挂上（`RealmChampionHookId`），场地用场域背景。
            string bossId = e.Id;
            if (string.IsNullOrEmpty(bossId))
            {
                foreach (var bd in BossCatalog.All)
                {
                    if (bd == null || !bd.Id.StartsWith("b_")) continue;
                    if (bd.Element == e.Element) { bossId = bd.Id; break; }
                }
                if (string.IsNullOrEmpty(bossId) && BossCatalog.All.Count > 0)
                    bossId = BossCatalog.All[0].Id;
            }
            var boss = BossCatalog.BuildBoss(bossId);
            if (boss == null) return null;

            // ★ 秘境首领：改成自己的 id/名字，并把 `UI/Realm/Champion/<本名>` 注册成立绘覆盖
            //   （用户实测指出这 10 张图本来就有，之前是我没找到）
            if (!string.IsNullOrEmpty(e.RealmId) && !string.IsNullOrEmpty(e.ChampionName))
            {
                boss.Id = "rc_" + (e.HookId ?? "x");
                boss.DisplayName = e.ChampionName;
                var sp = Resources.Load<Sprite>("UI/Realm/Champion/" + e.ChampionName);
                if (sp != null)
                {
                    BattleStage2D.PortraitOverrides[boss.Id] = sp;
                    Debug.Log("[BossRush] 秘境首领立绘已绑定：" + e.ChampionName);
                }
                else Debug.LogWarning("[BossRush] 缺立绘 UI/Realm/Champion/" + e.ChampionName);
            }

            var req = new BattleRequest
            {
                Title = "首领试炼 · " + e.Name,
                WeatherName = "试炼（无天时）",
                Seed = (ulong)Random.Range(1, int.MaxValue),
                IsBoss = true,
                RealmChampionHookId = string.IsNullOrEmpty(e.RealmId) ? null : e.HookId,
            };

            // 敌方：首领本体（站中排）
            // ★ 2026-10-25 两轮反馈合并：
            //   ① "血量太低，看不到机制" → 要加厚
            //   ② "伤害不要这么高，我一下就被打死" → ⛔ StatMul 会同时放大 生命/攻击/防御
            //   ⇒ 只加**生命**（直接抬 BaseHp），攻击防御保持原样。
            boss.BaseHp = Mathf.RoundToInt(boss.BaseHp * 30f);   // 2026-10-25：8 倍仍偏少，抬到 30 倍（只加血不加攻防）
            req.EnemyEntries.Add(new DeployEntry {
                Def = boss, Side = TeamSide.Enemy, PosIndex = 4, StatMul = 1f });

            // ★ ① 有对战码：解码成**真实阵容**（含站位 + 养成倍率）
            //   ⛔ 之前只把码存成字符串、没参与组队 —— 用户："载入了队伍还是没变"。
            if (!string.IsNullOrEmpty(_code) && _content != null)
            {
                var beasts = WanXiang.Fusion.ContentLibrary.BuildBeasts(_content);
                var souls = WanXiang.Fusion.ContentLibrary.BuildSouls(_content);
                var pay = new WanXiang.Fusion.SharePayload();
                if (beasts != null && souls != null &&
                    WanXiang.Fusion.ShareCode.TryDecode(_code, beasts.Length, souls.Length, out pay) &&
                    pay.BeastIndices != null && pay.BeastIndices.Length > 0)
                {
                    for (int i = 0; i < pay.BeastIndices.Length; i++)
                    {
                        int bi = pay.BeastIndices[i];
                        if (bi < 0 || bi >= beasts.Length) continue;
                        float mul = 1f;
                        if (pay.Growth != null && i < pay.Growth.Length)
                        {
                            WanXiang.Fusion.ShareCode.UnpackGrowth(pay.Growth[i], out int lv, out bool ev);
                            mul = WanXiang.Meta.MetaDefaults.CombatBonusMul(lv, ev);
                        }
                        req.Player.Add(beasts[bi]);
                        req.PlayerCells.Add(pay.BoardSlots != null && i < pay.BoardSlots.Length ? pay.BoardSlots[i] : i);
                        req.PlayerMulPer.Add(mul);
                    }
                    if (req.Player.Count > 0)
                    {
                        if (pay.RelicMul > 0f) req.PlayerMul *= pay.RelicMul;
                        Debug.Log("[BossRush] 对战码组队成功：" + req.Player.Count + " 只，RelicMul=" + pay.RelicMul);
                        return req;
                    }
                }
                Debug.LogWarning("[BossRush] 对战码解析失败，退回当前旅程队伍");
            }

            // 我方：优先当前队伍，其次 Collection 前 5
            var run = WanXiang.Run.RunSave.Current;
            var ids = new List<string>();
            if (run != null && run.Team != null && run.Team.Count > 0) ids.AddRange(run.Team);
            else if (run != null && run.Collection != null) ids.AddRange(run.Collection);
            if (ids.Count == 0 && _content != null)
            {
                var all = WanXiang.Fusion.ContentLibrary.BuildBeasts(_content);
                if (all != null) foreach (var b in all) ids.Add(b.Id);
            }
            if (ids.Count == 0) return null;

            var byId = new Dictionary<string, BeastDef>();
            if (_content != null)
            {
                var all = WanXiang.Fusion.ContentLibrary.BuildBeasts(_content);
                if (all != null) foreach (var b in all) byId[b.Id] = b;
            }

            // 默认站位：前排 3（0/1/2）+ 中排 2（3/4）
            int[] cells = { 0, 1, 2, 3, 4 };
            int n = 0;
            foreach (var id in ids)
            {
                if (n >= 5) break;
                BeastDef bd;
                if (!byId.TryGetValue(id, out bd) || bd == null) continue;
                req.Player.Add(bd);
                req.PlayerCells.Add(cells[n]);
                req.PlayerMulPer.Add(1f);
                n++;
            }
            if (req.Player.Count == 0) return null;
            return req;
        }

        private string _code;

        private void OnLoadCode()
        {
            var code = _inputCode != null ? (_inputCode.text ?? "").Trim() : "";
            if (string.IsNullOrEmpty(code)) { if (_tmpHint != null) _tmpHint.text = "先把对战码粘进来。"; return; }
            _code = code;
            if (_tmpHint != null) _tmpHint.text = "已载入对战码（长度 " + code.Length + "），挑战时优先用这套队伍。";
            RefreshTeamLabel();
        }

        private void RefreshTeamLabel()
        {
            if (_tmpTeam == null) return;
            var run = WanXiang.Run.RunSave.Current;
            int n = run != null && run.Team != null ? run.Team.Count : 0;
            _tmpTeam.text = n > 0
                ? "出战队伍：当前旅程队伍（" + n + " 只）"
                : "出战队伍：取拥有栏前 5 只（建议先在编队里配好）";
        }
    }
}
