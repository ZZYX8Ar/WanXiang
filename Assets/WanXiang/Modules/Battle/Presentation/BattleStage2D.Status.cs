// ============================================================================
//  万相 · 单位状态显示（血条/破绽/元素附着/溶解）（2026-11-18 代码骨架重构）
//  从 BattleStage2D.cs 原样搬入，逻辑零改动。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {
        /// <summary>元素附着：脚下圈 = 元素色（恒定亮度）；血条右侧 4 段小圈 = 层数。</summary>
        private void SetAura2D(UnitView2D v, WanXiang.Battle.Core.UnitSnapshot s)
        {
            bool on = s.AuraEl != Element.None && s.AuraStacks > 0 && s.Alive;

            // ⛔ 2026-11-01 用户："水还是不够蓝、4 格圈也不够亮" ——
            //   混白会把颜色洗淡（水混白变灰）。改成**专用的高饱和元素色**，
            //   与美术调色板（偏水墨、饱和度低）分开，保证在浅色背景上一眼可辨。
            Color bright = on ? AuraColorOf(s.AuraEl) : Color.white;
            bright.a = 1f;

            if (v.AuraDot != null)
            {
                if (v.AuraDot.gameObject.activeSelf != on) v.AuraDot.gameObject.SetActive(on);
                if (on && v.AuraMat != null)
                {
                    v.AuraMat.SetColor("_Color", bright);
                    // ⛔ 2026-11-01 用户："颜色别随层数变，不然水看着黑" ⇒ 亮度恒定
                    v.AuraMat.SetFloat("_Strength", 1.0f);
                }
            }
            if (v.AuraStacksRing != null)
            {
                if (v.AuraStacksRing.gameObject.activeSelf != on) v.AuraStacksRing.gameObject.SetActive(on);
                if (on)
                {
                    var sp = AuraRing(s.AuraStacks);
                    if (v.AuraStacksRing.sprite != sp) v.AuraStacksRing.sprite = sp;
                    v.AuraStacksRing.color = bright;
                }
            }
            if (v.AuraLabelRoot != null && v.AuraLabelRoot.activeSelf) v.AuraLabelRoot.SetActive(false);
        }

        /// <summary>血条按比例伸缩。<paramref name="v"/> 的填充宽度 = <see cref="HpBarWidth"/>（可在 Inspector 调）。</summary>
        /// <summary>
        /// 破绽/失衡条（2026-10-17）。三态：
        ///   progress &lt; 0 且未失衡 → 整条隐藏；破绽进度 → 青色条 + "破绽·X N/M"；
        ///   失衡 → **整条变金** + "失衡 ×倍率　剩 N 回合"（失衡是玩家最该盯的状态，视觉权重最高）。
        /// 填充的缩放/左对齐补偿与 <see cref="SetHpBar2D"/> 同一套（尺寸烘在 sprite 里，别再乘宽度）。
        /// </summary>
        private void SetFlawBar2D(UnitView2D v, string text, float progress, int unbalanced)
        {
            if (v.FlawBg == null || v.FlawFill == null || v.FlawLabel == null) return;
            bool staggered = unbalanced > 0;
            bool show = staggered || progress >= 0f;

            if (!show)
            {
                if (v.FlawBg.gameObject.activeSelf) v.FlawBg.gameObject.SetActive(false);
                if (v.FlawFill.gameObject.activeSelf) v.FlawFill.gameObject.SetActive(false);
                if (v.FlawLabel.gameObject.activeSelf) v.FlawLabel.gameObject.SetActive(false);
                v.FlawShownText = text;
                return;
            }

            if (!v.FlawBg.gameObject.activeSelf) v.FlawBg.gameObject.SetActive(true);
            if (!v.FlawFill.gameObject.activeSelf) v.FlawFill.gameObject.SetActive(true);
            if (!v.FlawLabel.gameObject.activeSelf) v.FlawLabel.gameObject.SetActive(true);

            if (v.FlawLabel.text != text) v.FlawLabel.text = text;
            v.FlawShownText = text;

            if (staggered)
            {
                if (v.FlawFill.color != FlawStaggerColor) v.FlawFill.color = FlawStaggerColor;
                float full = Mathf.Max(0.05f, HpBarWidth - 0.06f);
                var t = v.FlawFill.transform;
                t.localScale = new Vector3(1f, 1f, 1f);
                t.localPosition = new Vector3(0f, t.localPosition.y, t.localPosition.z);
            }
            else
            {
                if (v.FlawFill.color != FlawFillColor) v.FlawFill.color = FlawFillColor;
                float ratio = Mathf.Clamp01(progress);
                float full = Mathf.Max(0.05f, HpBarWidth - 0.06f);
                var t = v.FlawFill.transform;
                t.localScale = new Vector3(ratio, 1f, 1f);
                t.localPosition = new Vector3(-(full - full * ratio) * 0.5f, t.localPosition.y, t.localPosition.z);
            }
        }

        private void SetHpBar2D(UnitView2D v, int hp, int max)
        {
            if (v.HpFill == null || max <= 0) return;
            float ratio = Mathf.Clamp01((float)hp / max);
            float full = Mathf.Max(0.05f, HpBarWidth - 0.06f);   // ⛔ 必须跟 BuildUnitsFor 里创建 HpFill 的宽度一致

            // ⛔ 缩放必须走**归一化比例**，不能再乘 `full`：`MakeChildSprite` 是把尺寸
            //   **烘进 sprite 本身**的（贴图 = size×100 像素，localScale 保持 1）
            //   ， 再乘一次 `full`(3.34) 就是**双重应用**，填充条会宽 3.34 倍、从血槽两边冒出来。
            //   用户实测的两个现象都出自这里："血怎么两边同时扣" + "护盾和血重叠看不清"。
            var t = v.HpFill.transform;
            t.localScale = new Vector3(ratio, 1f, 1f);
            // 左端固定 ， 只从**右边**往回缩（掉血观感正确）
            t.localPosition = new Vector3(-(full - full * ratio) * 0.5f, t.localPosition.y, t.localPosition.z);
            v.HpFill.color = ratio > 0.5f ? BattlePalette.Vital
                          : ratio > 0.25f ? BattlePalette.Gold : BattlePalette.Crimson;
        }

        /// <summary>
        /// 取出状态串里**优先级最高**的那条的外观配置（<paramref name="ids"/> 形如 `freeze:2|burn:3`）。
        /// 没配置过的状态返回 null（不显示外观）。
        /// </summary>
        private StatusOverlay BestOverlayFor(string ids)
        {
            if (string.IsNullOrEmpty(ids) || StatusOverlays == null) return null;
            StatusOverlay best = null;
            var parts = ids.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                var kv = parts[i].Split(':');
                if (kv.Length < 1 || string.IsNullOrEmpty(kv[0])) continue;
                for (int k = 0; k < StatusOverlays.Length; k++)
                {
                    var d = StatusOverlays[k];
                    if (d == null || d.Id != kv[0]) continue;
                    if (best == null || d.Priority < best.Priority) best = d;
                    break;
                }
            }
            return best;
        }

        /// <summary>状态图标（头顶徽标里的图）缓存。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> StatusIconCache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>
        /// 头顶徽标的图标：`Resources/UI/StatusIcon/&lt;状态id&gt;.png` —— 与状态特效同一条"文件名 = id"口径。
        /// 把图丢进那个目录就生效，不用改表、不用改引用。缺图时上层会回退成「色块 + 缩写」。
        /// </summary>
        private static Sprite ResolveStatusIconArt(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Sprite s;
            if (StatusIconCache.TryGetValue(id, out s) && s != null) return s;
            s = Resources.Load<Sprite>("UI/StatusIcon/" + id);
            // ⛔ **不缓存 null**：否则"图还没放进去时的 null"会被记一辈子
            //   （编辑器里运行时丢图进去也永远取不到；本轮就踩到过，调试了很久）。
            if (s != null) StatusIconCache[id] = s;
            return s;
        }

        /// <summary>状态外观图缓存（id → Sprite；null 也缓存，避免每帧 Load）。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> StatusArtCache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>
        /// 取某状态的外观图：优先用表里拖进去的 `Art`，没有就去 `Resources/UI/StatusArt/&lt;id&gt;` 找。
        /// 「**文件名 = 状态 id**」， 出图后直接丢进那个目录即可生效，不用改任何代码/引用。
        /// </summary>
        private static Sprite ResolveStatusArt(string id, Sprite fromTable)
        {
            if (fromTable != null) return fromTable;
            if (string.IsNullOrEmpty(id)) return null;
            Sprite s;
            if (StatusArtCache.TryGetValue(id, out s) && s != null) return s;
            s = Resources.Load<Sprite>("UI/StatusArt/" + id);
            if (s != null) StatusArtCache[id] = s;   // 同上：**不缓存 null**
            return s;
        }

        /// <summary>
        /// 状态外观：把优先级最高的那条覆盖到立绘上（冰封 = 冰蓝罩 / 束缚 = 苔绿罩 / 燃烧 = 火橙罩…）。
        /// 有美术图就显示美术图（按立绘尺寸自动缩放，`Scale`/`OffsetY` 可调），
        /// 没图则用该状态的半透明色块兜底 —— **不挂任何美术也看得见**。
        private void SetOverlay2D(UnitView2D v, string ids)
        {
            if (v.Overlay == null || v.Body == null) return;
            // ★ 2026-10-09 用户定案：关闭状态特效大图，只用头顶小图标（见 StatusArtEnabled）。
            if (!StatusArtEnabled) { v.Overlay.enabled = false; return; }
            var best = BestOverlayFor(ids);
            string key = best != null ? best.Id : null;
            if (v.OverlayShown == key) return;                 // 变了才换，别每帧重建 sprite
            v.OverlayShown = key;
            if (best == null) { v.Overlay.enabled = false; return; }

            var art = ResolveStatusArt(best.Id, best.Art);
            Vector2 bodySize = v.Body.sprite != null ? v.Body.sprite.bounds.size : new Vector2(1f, 1.4f);
            // 说明：这里用的是**贴图本地尺寸**，看着“比显示尺寸大”，但**不用乘缩放** ——
            //   因为 Overlay 是 Body 的**子物体**（见创建处 SetParent(bodySr.transform)），
            //   父级缩放会自动乘进去 ， 最终显示尺寸 = 贴图尺寸 × localScale × 立绘缩放 = 立绘显示尺寸。
            //   （曾误判“少了 lossyScale”而乘了一次，结果特效反而小了近 4 倍，已回滚。）
            if (art != null)
            {
                var a = art.bounds.size;
                v.Overlay.sprite = art;
                v.Overlay.color = new Color(1f, 1f, 1f, Mathf.Clamp01(best.Tint.a * 1.6f));
                v.Overlay.transform.localScale = new Vector3(
                    a.x > 0.0001f ? bodySize.x / a.x * Mathf.Max(0.01f, best.Scale) : 1f,
                    a.y > 0.0001f ? bodySize.y / a.y * Mathf.Max(0.01f, best.Scale) : 1f, 1f);
            }
            else
            {
                // 兜底色块：SolidSprite(32,64) = 0.32×0.64 世界单位，按立绘尺寸放大
                v.Overlay.sprite = SolidSprite(Color.white, 32, 64, 0, Color.white);
                v.Overlay.color = best.Tint;
                v.Overlay.transform.localScale = new Vector3(bodySize.x / 0.32f, bodySize.y / 0.64f, 1f);
            }
            // OffsetY 是**立绘高度的百分比**（-0.1 = 往下挪身高的 10%）—— 比写死像素值好调、也与缩放无关
            v.Overlay.transform.localPosition = new Vector3(0f, best.OffsetY * bodySize.y, -0.02f);
            v.OverlayDef = best;
            // 特效动画材质（按状态共享；取不到 shader 则保持默认 sprite 材质 = 静态图，不会崩）
            var mat = OverlayMaterial(best);
            if (mat != null) v.Overlay.sharedMaterial = mat;
            v.Overlay.enabled = true;
        }

        /// <summary>
        /// 护盾指示：护盾 &gt; 0 时显示血条右侧的蓝牌 + 数值，否则整块收起。
        /// ⚠ 2026-10-13 起护盾**一个回合后自动消失**（BattleUnit.ShieldTurn + TickStatuses 清旧盾），
        ///    也会被伤害消耗；无论哪种归零，这里都靠帧流 s.Shield 自动收牌（见 ApplyFrame→SetShield2D）。
        /// 所以它是"还剩多少"的真实信息，必须显示。
        /// </summary>
        /// <summary>
        /// 死亡墨蚀材质（懒创建）。返回 false = 本机不可用（shader 缺失/不支持），调用方走兜底。
        /// </summary>
        private bool EnsureDissolveMat(UnitView2D v)
        {
            if (v == null) return false;
            // 兜底：Body 引用可能为空（某些 sp 变体只在 Root 下挂了 SpriteRenderer）
            if (v.Body == null && v.Root != null)
                v.Body = v.Root.GetComponentInChildren<SpriteRenderer>(true);
            if (v.Body == null)
            {
                if (!_dissolveWarned) { _dissolveWarned = true; Debug.LogWarning("[Stage2D] 墨蚀溶解跳过：找不到立绘 SpriteRenderer"); }
                return false;
            }
            if (v.DissolveMat != null) return true;

            var sh = Resources.Load<Shader>("Battle/InkDissolve");
            if (sh == null || !sh.isSupported)
            {
                if (!_dissolveWarned) { _dissolveWarned = true; Debug.LogWarning("[Stage2D] 墨蚀溶解不可用（shader 缺失/不支持），退回淡出"); }
                return false;
            }
            var noise = Resources.Load<Sprite>("Battle/FX_motion_fog");
            var mat = StageMaterialFactory.Create(StageMaterialKind.InkDissolve,
                new StageMaterialContext {
                    Shader = sh,
                    MainTexture = v.Body.sprite != null ? v.Body.sprite.texture : null,
                    NoiseTexture = noise != null ? noise.texture : null });
            if (mat == null)
            {
                if (!_dissolveWarned) { _dissolveWarned = true; Debug.LogWarning("[Stage2D] 墨蚀溶解材质创建失败，退回淡出"); }
                return false;
            }

            if (v.BodyMaterial == null) v.BodyMaterial = v.Body.sharedMaterial;
            v.Body.material = mat;
            v.DissolveMat = mat;
            if (!_dissolveLogDone)
            {
                _dissolveLogDone = true;
                Debug.Log("[Stage2D] 墨蚀溶解已挂到立绘：" + v.Body.name + " sprite=" + (v.Body.sprite != null ? v.Body.sprite.name : "null"));
            }
            return true;
        }
    }
}
