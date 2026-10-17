// ============================================================================
//  万相 · 秘境转场「艺术字揭示」Shader（2026-10-16）
//  ---------------------------------------------------------------------------
//  用户需求："重新设计这个转场，我建议使用粒子拨开，在这个粒子拨开的背景上加一些字…
//  这个字你可以参考 D:\Unity_Project\Demo\InkAbyssWalker-Integration 里面的开场字
//  「墨渊行者」，是使用了 shader 的…它这个字好像是生成好的图片，不是真正的字体，
//  你也可以使用这个模式，这样这些艺术字好看一点"
//
//  ⇒ 参考工程的 `Custom/LogoAppearWithNoise`：
//     **不是**用 TMP 排字，而是拿一张**预渲染的艺术字图**（毛笔字 + 墨点飞白），
//     用噪声扰动的**径向揭示**一笔笔"洇"出来。边缘还带一道发光涟漪。
//     这样字的美术上限由原图决定，不受字形/字体限制 —— 正是用户要的效果。
//
//  ⚠ 与参考工程的差异（必须做的适配）：
//    ① 参考用的是 URP（HLSLPROGRAM + Core.hlsl）；本工程 UI 层走**内置管线 CGPROGRAM**，
//       与 Resources/Shaders 里其它 UI shader 保持一致（换 URP 写法会和现有 UI 材质冲突）。
//    ② ⛔⛔ **UGUI 自定义 shader 必须把顶点色 i.color 与 _Color 乘进去** ——
//       否则运行时改 Image.color 完全无效（本项目 2026-10-12 踩过，写进约定了）。
//    ③ 用 `_MainTex_ST` 支持 Tiling/Offset（Image 的 uvRect 依赖它）。
//
//  两个可调的门面参数：
//    _Progress 0→1 揭示进度（转场里由代码推）
//    _RippleCenter 涟漪中心（默认落在字的重心附近，让"洇开"从主字起笔）
// ============================================================================

Shader "WanXiang/UI/RealmTitleReveal"
{
    Properties
    {
        _MainTex      ("Title Art (RGBA)", 2D) = "white" {}
        _Color        ("Tint", Color) = (1, 1, 1, 1)
        _NoiseTex     ("Noise (Grayscale)", 2D) = "gray" {}

        _Progress     ("Progress", Range(0, 1)) = 0
        _RippleCenter ("Ripple Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _Feather      ("Feather", Range(0.01, 0.5)) = 0.16
        _MaxRadius    ("Max Radius", Range(0.5, 2.5)) = 1.5

        _NoiseStrength("Noise Strength", Range(0, 0.5)) = 0.15
        _EdgeColor    ("Edge Color", Color) = (0.85, 0.78, 0.55, 1)
        _EdgeWidth    ("Edge Width", Range(0, 0.2)) = 0.035

        // ⛔⛔ 墨色反相开关（2026-10-17 加）：
        //   字图是**黑墨**画在白纸上的。如果转场底也是近黑（本工程就是黑幕），
        //   黑字直接"融进背景"——实测 _Progress=0.75 时整幅字只剩边缘那圈白线，
        //   字内部完全看不见，像没做出来。
        //   ⇒ _Invert=1 把 RGB 翻成 (1−RGB)，黑墨变白墨，在暗底上才立得住；
        //     alpha 不动，所以飞白/枯笔的镂空质感完全保留。
        _Invert       ("Invert Ink (0 = 黑墨, 1 = 白墨)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Default"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float2 uvNoise  : TEXCOORD1;
                fixed4 color    : COLOR;
            };

            sampler2D _MainTex;  float4 _MainTex_ST;
            sampler2D _NoiseTex; float4 _NoiseTex_ST;
            fixed4 _Color;
            float  _Progress;
            float4 _RippleCenter;
            float  _Feather;
            float  _MaxRadius;
            float  _NoiseStrength;
            fixed4 _EdgeColor;
            float  _EdgeWidth;
            float  _Invert;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                // 噪声用**独立**的 tiling，避免和字图的 uvRect 绑死
                o.uvNoise = TRANSFORM_TEX(v.texcoord, _NoiseTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ---- 1. 噪声扰动：把完美的圆形边界打碎成水墨不规则边缘 ----
                float noise = tex2D(_NoiseTex, i.uvNoise).r;
                float noiseOffset = (noise - 0.5) * _NoiseStrength;

                float dist = distance(i.uv, _RippleCenter.xy) + noiseOffset;

                // ---- 2. 当前揭示半径 ----
                float currentRadius = _Progress * _MaxRadius;

                // ---- 3. 可见性遮罩（软化边缘）----
                float mask = 1.0 - smoothstep(currentRadius - _Feather, currentRadius, dist);
                mask *= step(0.0001, _Progress);   // Progress=0 时避免浮点误差露出残影

                // ---- 4. 字图 ----
                fixed4 col = tex2D(_MainTex, i.uv);

                // ★ 墨色反相（在乘 tint 之前做：tint 是"导演意图"，不该被反相翻掉）
                //   只翻 RGB，alpha 不动 —— 飞白/枯笔的镂空全靠 alpha，翻不翻都一样。
                col.rgb = lerp(col.rgb, 1.0 - col.rgb, _Invert);

                // ⛔ 必须乘顶点色 × _Color：UGUI 靠这个改颜色，漏了就是"代码改了色但没反应"
                col *= i.color;

                // ---- 5. 涟漪边缘发光（顺着被噪声扰动的边界，所以也会弯曲）----
                float edge = saturate(1.0 - abs(dist - currentRadius) / (_EdgeWidth + 0.0001));
                edge *= step(0.0001, _Progress) * (1.0 - step(0.999, _Progress));  // 揭示完成时收掉
                edge *= mask;

                col.rgb = lerp(col.rgb, _EdgeColor.rgb, edge * _EdgeColor.a * col.a);
                col.a *= mask;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
