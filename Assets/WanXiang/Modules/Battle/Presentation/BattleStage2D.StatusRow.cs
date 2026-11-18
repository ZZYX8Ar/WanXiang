// ============================================================================
//  万相 · 状态行 / 状态光环显示（2026-11-18 从 BattleStage2D.cs 拆出）
//  行为零变化：仅移动方法。共享字段在 Fields.cs，partial 内互通。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {
        /// <summary>
        /// 状态色光环（脚下一圈，灼烧/冻结/湿/毒…）。层数越高越亮。
        /// ⚠ 画在立绘**之下**（sortingOrder = body - 1），只借噪声图、纯程序化。
        /// </summary>
        private void SetStatusAura2D(UnitView2D v, string ids)
        {
            if (v == null || v.Root == null || v.Body == null) return;

            // 解析最高优先级状态
            string key = null; int stacks = 0;
            if (!string.IsNullOrEmpty(ids))
            {
                string best = null; int bestPri = -1;
                foreach (var part in ids.Split('|'))
                {
                    var kv = part.Split(':');
                    if (kv.Length < 1 || string.IsNullOrEmpty(kv[0])) continue;
                    int st = kv.Length > 1 && int.TryParse(kv[1], out var n) ? Mathf.Max(1, n) : 1;
                    int pri = kv[0] == "burn" ? 5 : kv[0] == "freeze" ? 4 : kv[0] == "wet" ? 3
                            : kv[0] == "poison" ? 2 : 1;
                    if (pri > bestPri) { bestPri = pri; best = kv[0]; stacks = st; }
                }
                key = best;
            }

            if (key == null)
            {
                // ⛔ 只隐藏、**不销毁材质**：销毁后 SpriteRenderer 还在，
                //   下次状态回来会跳过创建直接 SetColor ⇒ NullReferenceException（用户实测报障）。
                if (v.StatusAura != null && v.StatusAura.gameObject.activeSelf)
                    v.StatusAura.gameObject.SetActive(false);
                v.StatusAuraKey = null;
                return;
            }

            if (v.StatusAura == null || v.StatusAuraMat == null)
            {
                var sh = Resources.Load<Shader>("Battle/StatusAura");
                var sp = Resources.Load<Sprite>("Battle/FX_shield_hex");   // 只用它的矩形 mesh
                var noise = Resources.Load<Sprite>("Battle/FX_motion_fog");
                if (sh == null || sp == null || !sh.isSupported) return;

                var go = new GameObject("StatusAura");
                go.transform.SetParent(v.Body.transform, false);
                // ⛔ localPosition 在 Body 本地空间（单位=贴图 bounds），-0.42 会缩到没有；
                //   用 sprite 高度的一半再往下挪，才是"脚底"。
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = v.Body.sortingOrder - 1;

                // R2：与 AuraFoot 共用同一套布局公式（只换宽高倍率）。
                // ⛔ 内部已除以父级 lossyScale —— Body 在 Root 0.1 缩放之下，漏了会小到看不见。
                var auraLayout = StageAuraLayout.Compute(v.Body.sprite,
                    v.Body.transform.lossyScale, sp, 1.5f, 0.75f);
                go.transform.localPosition = auraLayout.LocalPosition;
                go.transform.localScale = auraLayout.LocalScale;

                var mat = StageMaterialFactory.Create(StageMaterialKind.StatusAura,
                    new StageMaterialContext {
                        Shader = sh,
                        NoiseTexture = noise != null ? noise.texture : null });
                if (mat != null)
                {
                    sr.material = mat;
                    v.StatusAura = sr;
                    v.StatusAuraMat = mat;
                }
            }

            // 颜色 / 强度（层数越高越亮）
            Color c;
            switch (key)
            {
                case "burn":   c = new Color(1.00f, 0.45f, 0.16f); break;
                case "freeze": c = new Color(0.55f, 0.85f, 1.00f); break;
                case "wet":    c = new Color(0.30f, 0.62f, 0.98f); break;
                case "poison": c = new Color(0.58f, 0.80f, 0.30f); break;
                default:       c = new Color(0.80f, 0.80f, 0.82f); break;
            }
            if (v.StatusAura == null || v.StatusAuraMat == null) return;   // 兜底：建不出来就别往下写
            v.StatusAuraMat.SetColor("_Color", c);
            v.StatusAuraMat.SetFloat("_Strength", Mathf.Clamp01(0.55f + 0.15f * (stacks - 1)));
            v.StatusAura.gameObject.SetActive(true);
            v.StatusAuraKey = key;
        }

        public bool TryGetViewStatusIds(string unitId, out string ids)
        {
            ids = null;
            if (string.IsNullOrEmpty(unitId) || _views == null) return false;
            UnitView2D v;
            if (!_views.TryGetValue(unitId, out v) || v == null) return false;
            ids = v.StatusShown;
            return true;
        }

        /// <summary>
        /// 重画某单位的**状态图标排**。<paramref name="ids"/> 形如 `burn:3|root:1`。
        /// 图标查 <see cref="StatusIcons"/>（Id 同时也是 `ArtRes/UI/Status/<id>.png` 的文件名 ， 换图即换美术）；
        /// 查不到或没拖图 ， 回退成「色块 + 缩写 + 层数」，绝不空白。
        /// </summary>
        private void UpdateStatusRow(UnitView2D v, string ids)
        {
            v.StatusShown = ids;
            int used = 0;
            if (!string.IsNullOrEmpty(ids) && v.StatusPlates != null)
            {
                var parts = ids.Split('|');
                for (int i = 0; i < parts.Length && used < v.StatusPlates.Length; i++)
                {
                    var kv = parts[i].Split(':');
                    if (kv.Length < 2) continue;
                    int stacks = 1;
                    int.TryParse(kv[1], out stacks);
                    var def = StatusIcons != null ? System.Array.Find(StatusIcons, x => x != null && x.Id == kv[0]) : null;
                    var plate = v.StatusPlates[used];
                    var label = v.StatusLabels != null ? v.StatusLabels[used] : null;
                    if (plate == null) continue;

                    // 图标来源与状态特效同一条口径：表里拖的 `Icon` 优先 → `Resources/UI/StatusIcon/<id>` → 色块+缩写
                    var iconArt = def != null ? def.Icon : null;
                    if (iconArt == null) iconArt = ResolveStatusIconArt(kv[0]);
                    if (iconArt != null)
                    {
                        plate.sprite = iconArt;           // ← 你拖进来 / 放进 Resources 的图
                        plate.color = Color.white;
                        if (label != null) label.text = "";
                    }
                    else
                    {
                        plate.sprite = SolidSprite(Color.white, 32, 32, 2, new Color(0.10f, 0.08f, 0.06f));
                        plate.color = def != null ? def.Color : new Color(0.6f, 0.6f, 0.6f);
                        if (label != null)
                        {
                            // ⛔ 原来回退用的是状态 id（英文，如 "shock"）—— 用户看到"shock"截图。
                            //   改为：Abbr → 中文名首字 → id 首字母。
                            var sdef = WanXiang.Battle.Core.StatusCatalog.Get(kv[0]);
                            string nm = def != null && !string.IsNullOrEmpty(def.Abbr) ? def.Abbr
                                      : (!string.IsNullOrEmpty(sdef.Name) ? sdef.Name.Substring(0, 1)
                                                                                          : kv[0]);
                            label.text = nm + (stacks > 1 ? stacks.ToString() : "");
                        }
                    }
                    // ★ 按图的实际尺寸归一化（2026-10-09 根因修复）：
                    //   槽位缩放 = StatusIconSize / 图宽 ， 图标世界尺寸恒等于 StatusIconSize。
                    //   此前槽位缩放写死按"底图 32px"算（0.36/0.32≈1.13），而 StatusIcon/*.png 实为
                    //   256×256（2.56 世界单位）， 图标被放大 8 倍 ≈2.9 单位、盖满整格 ——
                    //   用户看到的"巨大盾牌/漩涡/火焰"就是它们（不是特效 overlay，那个一直是正常的）。
                    {
                        var drawn = plate.sprite;
                        float srcW = drawn != null ? drawn.bounds.size.x : 0f;
                        float sc = srcW > 0.0001f ? StatusIconSize / srcW : 1.125f;
                        if (plate.transform.parent != null)
                            plate.transform.parent.localScale = new Vector3(sc, sc, 1f);
                    }
                    plate.gameObject.SetActive(true);
                    if (plate.transform.parent != null) plate.transform.parent.gameObject.SetActive(true);
                    used++;
                }
            }
            for (int s = used; s < (v.StatusPlates != null ? v.StatusPlates.Length : 0); s++)
                if (v.StatusPlates[s] != null && v.StatusPlates[s].transform.parent != null)
                    v.StatusPlates[s].transform.parent.gameObject.SetActive(false);

            // 居中排布。⛔ **不要再除以行缩放**：`rowGo.localScale = 1/rootScale` 的存在意义
            //   正是让**它的局部空间 == 世界单位**（rowGo.lossyScale = (1/rootScale) × rootScale = 1）。
            //   再除一次就缩小了 ~rootScale 倍（实测 9.75×）， 三个状态挤在同一处看不出区别（用户截图报的"叠加"）。
            if (used > 0 && v.StatusRow != null)
            {
                float step = StatusIconSize + StatusIconGap;
                for (int s = 0; s < used; s++)
                    if (v.StatusPlates[s] != null && v.StatusPlates[s].transform.parent != null)
                        v.StatusPlates[s].transform.parent.localPosition =
                            new Vector3((s - (used - 1) * 0.5f) * step, 0f, 0f);
            }
        }

        /// <summary>
        /// 中途增员的**惰性补建**：按 UnitId 到表现层名册（AllUnitsEver）里找到它，就地建一个 view。
        /// 找不到（已下场 / id 不对）返回 null。用法与原因见 <see cref="BuildUnitsFor"/> 的注释。
        /// </summary>
    }
}
