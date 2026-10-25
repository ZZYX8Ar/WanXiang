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
                var row = Instantiate(_itemTemplate, _rootList);
                row.gameObject.SetActive(true);
                row.name = "Item_" + (string.IsNullOrEmpty(e.Id) ? e.RealmId + "_" + i : e.Id);
                _rows.Add(row.gameObject);

                // 分组标题：第一行或分组变化时，名字前缀带上组名
                string group = BossRushCatalog.GroupTitle(e);
                bool newGroup = i == 0 || BossRushCatalog.GroupTitle(all[i - 1]) != group;

                var txts = row.GetComponentsInChildren<TMP_Text>(true);
                // 约定：0 = 名字（大），1 = 标签（小）
                if (txts.Length > 0)
                    txts[0].text = (newGroup ? "<size=70%><color=#8A7A5F>" + group + "</color></size>\n" : "") + e.Name;
                if (txts.Length > 1)
                    txts[1].text = e.Playable ? ElementLabel(e.Element) : "接入中";

                // 立绘小图标
                var img = row.GetComponentInChildren<Image>(true);
                if (img != null && _sprites != null && !string.IsNullOrEmpty(e.Id))
                {
                    var sp = _sprites.GetHead(e.Id) ?? _sprites.Get(e.Id);
                    if (sp != null) { img.sprite = sp; img.preserveAspect = true; img.color = Color.white; }
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
            }
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
            var boss = BossCatalog.BuildBoss(e.Id);
            if (boss == null) return null;

            var req = new BattleRequest
            {
                Title = "首领试炼 · " + e.Name,
                WeatherName = "试炼（无天时）",
                Seed = (ulong)Random.Range(1, int.MaxValue),
                IsBoss = true,
            };

            // 敌方：首领本体（站中排）
            req.EnemyEntries.Add(new DeployEntry { Def = boss, Side = TeamSide.Enemy, PosIndex = 4, StatMul = 1f });

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
