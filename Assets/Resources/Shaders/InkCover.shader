// ============================================================================
//  万相 · 转场遮罩「墨从四边洇入」Shader（2026-11-04，T2）
//  ---------------------------------------------------------------------------
//  要解决什么：RealmTransition 原来的遮罩是一张**纯色 Image**，靠 alpha 0→1→0 淡入淡出。
//  实机观感是"整屏均匀压黑"，没有墨迹的边，也没有"纸被墨吃进去"的过程。
//
//  本版做法（与 Resources/Shaders 其它 UI shader 一致：内置管线 CGPROGRAM）：
//   ① 四边同时向内推进：边缘距离 = min(u, 1-u, v, 1-v)，等值线是"一圈圈方框"，
//      而不是从中心扩散的圆 —— 这是"四面洇入"与"中心涟漪"的根本区别；
//   ② 双频噪声打碎边界：低频做大块墨渍起伏、高频做纸纤维毛边；
//   ③ 撕裂线：一层低频 sin 让某些位置"窜得更快"，避免四条边整齐得像贴纸；
//   ④ 湿边：墨前沿向内一小段**偏冷、更实**（_RimTint / _RimStrength）；
//   ⑤ 前渗：前沿**外侧**一层半透明墨痕（_Bleed / _BleedAlpha），像宣纸先吸水再变黑；
//   ⑥ Progress=0 时 alpha 严格为 0（不留残影）；Progress=1 时整屏实心。
//
//  ⛔ 色彩空间：本工程是 **Linear**。`_CoverColor` 传纯黑最安全；
//     想用"墨青"就把 RGB 压到 0.02~0.06 线性（≈ sRGB 0.15~0.28），别写 0.2。
//  ⛔ 转场期间 `Image.raycastTarget` 必须为 true，结束时置 false（由 RealmTransition 负责）。
// ============================================================================
Shader "WanXiang/UI/InkCover"
{
    Properties
    {
        _MainTex       ("Sprite (unused, keep white)", 2D) = "white" {}
        _Color         ("Tint", Color) = (1,1,1,1)

        [Header(Ink)]
        _CoverColor    ("Ink Color", Color) = (0,0,0,1)
        _Progress      ("Progress (0=透明 1=全墨)", Range(0,1)) = 0

        [Header(Edge)]
        _NoiseScale    ("Noise Scale", Range(1,60)) = 7
        _NoiseStrength ("Edge Noise", Range(0,0.5)) = 0.11
        _Tear          ("Tear", Range(0,0.2)) = 0.04
        _Soft          ("Edge Softness", Range(0.002,0.2)) = 0.010

        [Header(Bleed)]
        _Bleed         ("Bleed Ahead", Range(0,0.2)) = 0.022
        _BleedAlpha    ("Bleed Alpha", Range(0,1)) = 0.10
        _RimWet        ("Wet Rim Width", Range(0,0.2)) = 0.020
        _RimTint       ("Wet Rim Tint", Color) = (0.22,0.24,0.28,1)
        _RimStrength   ("Wet Rim Strength", Range(0,1)) = 0.35

        [Header(LifeAndTexture)]
        _FlowSpeed     ("Noise Flow Speed", Range(0,0.6)) = 0.05
        _EdgeWeight    ("Edge Weights (L,R,B,T)", Vector) = (1.10,0.92,1.04,0.96)
        _InkDensity    ("Ink Density Var", Range(0,0.25)) = 0.0
        _DryBrush      ("Dry Brush", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
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
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;

            fixed4 _CoverColor;
            float  _Progress;

            float  _NoiseScale;
            float  _NoiseStrength;
            float  _Tear;
            float  _Soft;

            float  _Bleed;
            float  _BleedAlpha;
            float  _RimWet;
            fixed4 _RimTint;
            float  _RimStrength;
            float  _FlowSpeed;
            float4 _EdgeWeight;
            float  _InkDensity;
            float  _DryBrush;

            // ---- 程序化噪声（不用贴图：遮罩是全屏的，采样省一次 texture fetch 也不占资源位）----
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);        // smoothstep 插值
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float flow = _Time.y * _FlowSpeed;      // 噪声随时间缓慢漂移 ⇒ 墨缘"活着"

                // ① 到最近边的距离，但**四边权重不同**（0.94~1.06）：
                //    完全对称的推进一眼就假；让下边快一点、左边慢一点，像被纸纤维牵着走。
                float eL = uv.x        * _EdgeWeight.x;
                float eR = (1.0 - uv.x) * _EdgeWeight.y;
                float eB = uv.y        * _EdgeWeight.z;
                float eT = (1.0 - uv.y) * _EdgeWeight.w;
                float edge = min(min(eL, eR), min(eB, eT));

                // ② 双频噪声：低频大墨渍 + 高频纸纤维（都带 flow，边界持续微动）
                float n1 = vnoise(uv * _NoiseScale + flow);
                float n2 = vnoise(uv * _NoiseScale * 3.7 + 11.3 - flow * 0.7);
                float n  = n1 * 0.70 + n2 * 0.30;
                float wobble = (n - 0.5) * _NoiseStrength;

                // ③ 大块不对称：两个**低频** sin 分别给 X/Y 方向一个大起伏（墨在一侧积得厚）。
                //   ⛔ 不要用高频 sin 做"撕裂"：周期太短会在墨里切出细白条（实测像噪点/裂纹）。
                float tear = (sin(uv.x * 6.1 + flow * 1.3) + sin(uv.y * 4.7 - flow * 0.9)) * 0.5 * _Tear;

                // ④ 前沿：pow 让"起手慢、中段冲、收笔稳"，别用线性（线性最像 PPT）
                float on = step(0.0001, _Progress);
                float front = pow(saturate(_Progress), 1.15) * 0.62;
                float d = front - (edge + wobble + tear);     // d>0 = 已被墨吃掉

                // ⑤ 主体：边缘 _Soft 内柔化
                float cover = smoothstep(-_Soft, _Soft, d) * on;

                // ⑥ 墨色分层：低频浓淡（0.93~1.0），避免整片死黑 —— 这是"高级感"的关键
                float dens = vnoise(uv * 2.1 + flow * 0.4);
                cover *= 1.0 - _InkDensity * (1.0 - dens);

                // ⑦ 前渗：前沿外侧一层半透明墨痕（宣纸先吸水）
                float bleed = saturate(1.0 - abs(d) / max(_Bleed, 1e-4));
                bleed *= step(d, 0.0001) * on;
                float a = saturate(cover + bleed * _BleedAlpha * (1.0 - cover));

                // ⑧ 飞白：只有**前沿内侧一条窄带**才挖半透小洞，像干笔擦过纸面。
                //    ⛔ Progress 接近 1 时必须收掉，否则整屏会留一圈"筛子"。
                float brushBand = saturate(1.0 - abs(d - 0.028) / 0.030);
                brushBand *= on * (1.0 - smoothstep(0.86, 0.98, _Progress));
                float dry = step(0.60, vnoise(uv * 26.0 + flow * 2.0));
                a *= 1.0 - dry * brushBand * _DryBrush * 0.55;

                // ⑨ 湿边：前沿向内一小段更冷更实
                float rim = saturate(1.0 - abs(d) / max(_RimWet, 1e-4));
                rim *= on * (1.0 - step(0.999, _Progress));

                float3 col = lerp(_CoverColor.rgb, _RimTint.rgb, saturate(rim * _RimStrength));
                col *= i.color.rgb;

                return fixed4(col, saturate(a * _CoverColor.a * i.color.a));
            }
            ENDCG
        }
    }
    Fallback Off
}
