// ============================================================================
//  万相 · 招架/QTE 演出（2026-11-17 代码骨架重构）
//  从 BattleStage2D.cs 原样搬入，逻辑零改动。
//  ⚠ 共享字段（_parry* / _views / _cam 等）保留在主文件，partial 直接访问。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {
        /// <summary>打开格挡提示（面板在 NeedParry 时调；跟随与点亮在 Step 里做）。</summary>
        public void ShowParryPrompt(string attackerId)
        {
            _parryActorId = attackerId;
            _parryK = 0f;
            EnsureParryRings();
            if (_parryRing != null) _parryRing.enabled = false;
            if (_parryTarget != null) _parryTarget.enabled = false;
        }

        /// <summary>喂环收缩进度（0..1）。</summary>
        public void SetParryProgress(float k) { _parryK = Mathf.Clamp01(k); }

        /// <summary>关闭提示圈。</summary>
        public void HideParryPrompt()
        {
            _parryActorId = null;
            if (_parryRing != null) _parryRing.enabled = false;
            if (_parryTarget != null) _parryTarget.enabled = false;
        }

        // ================================================================
        //  格挡成功「爆闪」（2026-10-15 用户："格挡成功的反馈我好像没有怎么体会到"）
        //  ----------------------------------------------------------------
        //  之前格挡成功只有：① 头顶飘"完美格挡!"小字 ② 轻微震屏。玩家说"没体会到"。
        //  现在在下场者（被格挡的攻击者）身上炸一圈**扩散光环 + 中心闪光**：
        //    · 完美档：金色大光环 + 双环 + 白闪，扩得大、留得久
        //    · 普通档：青白小环，快收
        //  ⚠ 由 Step(dt) 推进 —— dt=0（顿帧）时也会停住，那正是"顿"手感的一部分。
        // ================================================================
        /// <summary>R3-c：格挡爆闪池（Spawn/Step/ClearRefs，与 FloatingTextPool 同模式）。</summary>
        private static Sprite _parryDiscSprite;   // 实心圆盘（中心闪光）

        /// <summary>
        /// 格挡成功爆闪。grade：0=未挡（不爆）/1=普通/2=完美。
        /// </summary>
        public void ParryImpact(string unitId, int grade)
        {
            if (grade <= 0 || string.IsNullOrEmpty(unitId) || _views == null) return;
            UnitView2D v;
            if (!_views.TryGetValue(unitId, out v) || v == null || v.Root == null) return;

            Vector3 p = v.Root.transform.position + new Vector3(0f, 0.9f, -0.6f);
            bool perfect = grade >= 2;
            EnsureParryRings();
            if (_parryRingSprite == null) _parryRingSprite = BuildRingSprite(128, 6);
            if (_parryDiscSprite == null) _parryDiscSprite = BuildDiscSprite(96);

            if (perfect)
            {
                // 完美：外圈金环（扩得大）+ 内圈白环（快）+ 中心闪光
                _burstPool.Spawn(transform, p, _parryRingSprite, new Color(1f, 0.84f, 0.38f, 0.95f), 0.35f, 3.1f, 0.42f, 30);
                _burstPool.Spawn(transform, p, _parryRingSprite, new Color(1f, 1f, 0.94f, 0.90f), 0.25f, 1.9f, 0.26f, 31);
                _burstPool.Spawn(transform, p, _parryDiscSprite, new Color(1f, 0.96f, 0.80f, 0.75f), 0.30f, 1.35f, 0.20f, 29);
            }
            else
            {
                // 普通：一圈青白环
                _burstPool.Spawn(transform, p, _parryRingSprite, new Color(0.72f, 0.92f, 1f, 0.85f), 0.25f, 1.7f, 0.28f, 30);
            }
        }


        /// <summary>程序化**实心圆盘**贴图（中心亮、边缘衰减；格挡中心闪光用）。</summary>
        private static Sprite BuildDiscSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                float a = Mathf.Clamp01(1f - d);
                a *= a;
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>是否正在播大招 cut-in（格挡窗要等它播完再弹，用户 2026-10-15）。</summary>


/// <summary>判定反馈：在被判定者头顶飘字 + **屏幕中央横幅**。
        /// 格挡 = 完美格挡!/格挡/未挡；终结技 = 共鸣·强化/共鸣/失谐·威力减弱；连携 = 阵成/结阵/散阵。
        ///
      /// ★★ 2026-10-16 用户："判断成功和失败，加强和减弱能在醒目一点吗，
        ///   还是说是太快了，导致看的不是很清楚" —— 两处一起改：
        ///     ① 头顶飘字寿命从 0.7s 拉到 1.25s（判定类文字要看得完）；
        ///     ② 加屏幕中央**大横幅**（弹入 + 停留 0.62s + 淡出），成功用金/青白、
        ///        失败用暗红，两种颜色对立，一眼能分清"这次打没打准"。
   /// </summary>
        public void ParryFeedback(string attackerId, int grade, bool finisher = false, bool combo = false)
        {
            if (string.IsNullOrEmpty(attackerId) || _views == null) return;
    UnitView2D v;
      if (!_views.TryGetValue(attackerId, out v) || v == null || v.Root == null) return;
       string banner; Color bc;
     if (combo)
      {
      // 阵形连携逐段反馈（写在施法者头上；成败由模拟层合并判定，这里只报单段手感）
  if (grade == 2) { banner = "阵成"; bc = new Color(1f, 0.85f, 0.45f); SpawnNumberInternal(v, "阵成!", bc, 1.7f, JudgeLife); }
         else if (grade == 1) { banner = "结阵"; bc = new Color(0.85f, 0.92f, 1f); SpawnNumberInternal(v, "结阵", bc, 1.4f, JudgeLife); }
                else { banner = "散阵"; bc = new Color(0.86f, 0.36f, 0.32f); SpawnNumberInternal(v, "散阵", bc, 1.4f, JudgeLife); }
                ShowVerdict(banner, bc);
         return;
  }
      if (finisher)
            {
        // 终结技 QTE：飘在被施放者（我方）头上
    if (grade == 2) { banner = "共鸣 · 威力强化"; bc = new Color(1f, 0.82f, 0.42f); SpawnNumberInternal(v, "共鸣! 强化", bc, 1.7f, JudgeLife); }
    else if (grade == 1) { banner = "共鸣"; bc = new Color(1f, 0.90f, 0.62f); SpawnNumberInternal(v, "共鸣", bc, 1.4f, JudgeLife); }
          else { banner = "失谐 · 威力减弱"; bc = new Color(0.92f, 0.40f, 0.34f); SpawnNumberInternal(v, "失谐! 威力减弱", bc, 1.5f, JudgeLife); }
                ShowVerdict(banner, bc);
         return;
            }
          if (grade == 2) { banner = "完美格挡"; bc = new Color(1f, 0.85f, 0.35f); SpawnNumberInternal(v, "完美格挡!", bc, 1.7f, JudgeLife); }
   else if (grade == 1) { banner = "格挡"; bc = new Color(0.72f, 0.88f, 1f); SpawnNumberInternal(v, "格挡", bc, 1.4f, JudgeLife); }
       else { banner = "未挡"; bc = new Color(0.90f, 0.38f, 0.32f); SpawnNumberInternal(v, "未挡", bc, 1.4f, JudgeLife); }
            ShowVerdict(banner, bc);
  }

        /// <summary>判定飘字的寿命（比伤害数字 0.7s 长一截：判定结果要"看得完"）。</summary>

        /// <summary>
      /// 屏幕中央判定横幅。成功/失败各一套颜色，由 <see cref="ParryFeedback"/> 调。
        /// ⛔ 纯表现层，不读战斗状态（帧流纪律）。
        /// </summary>
private void ShowVerdict(string text, Color c) => _verdictBanner.Show(text, c, transform);

        private void StepVerdict(float dt) => _verdictBanner.Step(dt);

        /// <summary>R3-b：QTE 提示条（窗口期常亮 + 底衬自适应）整体抽出。</summary>

        /// <summary>
        /// 设置 QTE 提示条文案。
        /// <paramref name="hitsTotal"/> = 0 表示单发（普通格挡）；&gt;0 表示多段连击，显示"第 k/n 段"。
        /// </summary>
        /// <summary>
        /// 起手预警的**谱系色块**（设计文档 2.2）。映射固定，玩久了形成条件反射：
        ///   赤=劈斩 / 橙=横扫 / 黄=砸地 / 青=贯穿 / 紫=吐息。
        /// ⛔ 用文本首字判定（"青·贯穿 1/2"）而不是回查 state —— 表现层不许读 battle 状态。
        /// </summary>
        private Color TelegraphColorFor(string telegraph)
        {
            if (string.IsNullOrEmpty(telegraph)) return TelegraphColor;
            switch (telegraph[0])
            {
                case '赤': return new Color(0.90f, 0.26f, 0.20f);   // 劈斩
                case '橙': return new Color(0.96f, 0.56f, 0.18f);   // 横扫
                case '黄': return new Color(0.94f, 0.84f, 0.26f);   // 砸地
                case '青': return new Color(0.32f, 0.80f, 0.84f);   // 贯穿
                case '紫': return new Color(0.76f, 0.52f, 0.92f);   // 吐息
                default: return TelegraphColor;
            }
        }

        public void SetQteHint(string text, int hitsTotal = 0, int hitsDone = 0)
            => _qtePrompt.Set(text, hitsTotal, hitsDone, transform);

        /// <summary>清空 QTE 提示（连击段间调用；文字置空 → 下一帧淡出）。</summary>
        public void ClearQteHint() => _qtePrompt.ClearCurrent();



        private void EnsureParryRings()
        {
            if (_parryRing == null)
            {
                if (_parryRingSprite == null) _parryRingSprite = BuildRingSprite(128, 6);
                var go = new GameObject("ParryRing");
                go.transform.SetParent(transform, false);
                _parryRing = go.AddComponent<SpriteRenderer>();
                _parryRing.sprite = _parryRingSprite;
                _parryRing.sortingOrder = 26;             // 与行动箭头同层，压过立绘
                _parryRing.color = new Color(1f, 0.90f, 0.55f, 0.95f);
                _parryRing.enabled = false;
            }
            if (_parryTarget == null)
            {
                if (_parryTargetSprite == null) _parryTargetSprite = BuildRingSprite(128, 3);
                var go = new GameObject("ParryTarget");
                go.transform.SetParent(transform, false);
                _parryTarget = go.AddComponent<SpriteRenderer>();
                _parryTarget.sprite = _parryTargetSprite;
                _parryTarget.sortingOrder = 26;
                _parryTarget.color = new Color(1f, 1f, 1f, 0.45f);
                _parryTarget.enabled = false;
            }
        }

        // 程序化"圆环"贴图（白色；尺寸与线宽像素级，运行时靠 localScale 缩放）
        private static Sprite BuildRingSprite(int size, int thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            float rOut = size * 0.5f - 1f;
            float rIn = rOut - thickness;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
 {
 float dx = x - c, dy = y - c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
       px[y * size + x] = (d <= rOut && d >= rIn) ? Color.white : Color.clear;
        }
   tex.SetPixels(px); tex.Apply();
    return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>程序化**实心方块**贴图（白色；给 QTE 提示条当底衬用）。</summary>
        private static Sprite BuildSolidSprite(int size)
   {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
       tex.SetPixels(px); tex.Apply();
return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
   }

    }
}
