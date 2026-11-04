// ============================================================================
//  万相 · 转场遮罩「墨从四边洇入」Shader（2026-11-04，T2）
//  ---------------------------------------------------------------------------
//  要解决什么：RealmTransition 原来的遮罩是一张**纯色 Image**，靠 alpha 0→1→0 淡入淡出。
//  实机观感是"整屏均匀压黑"，没有墨迹的边，也没有"纸被墨吃进去"的过程。
//
//  本版做法（与 Resources/Shaders 其它 UI shader 一致：内置管线 CGPROGRAM）：
//   ① 四边同时向内推进：边缘距离 = min(u, 1-u, v, 1-v)，等值线是"一圈圈方框"，
//      而不是从中心扩散的圆 —— 这是"四面洇入"与"中心涟漪"的根本区别；
//   ①b **域扭曲（domain warp）**：先用低频 fbm 把采样坐标推开，再算距离场。
//      这是 2026-11-04 第二版新增的关键一步 —— 只推阈值不扭坐标，边界永远是
//      "规则波浪"（用户："还不如之前那个"），扭了坐标才会拐弯、分叉、有墨渍的形状；
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
        _NoiseScale    ("Noise Scale", Range(1,60)) = 5
        _NoiseStrength ("Edge Noise", Range(0,0.5)) = 0.16
        _Warp          ("Domain Warp", Range(0,0.2)) = 0.045
        _Fray          ("Edge Fray (fine tendrils)", Range(0,0.2)) = 0.06
        _Soft          ("Edge Softness", Range(0.002,0.2)) = 0.008
        _EdgeWeight    ("Edge Weights (L,R,B,T)", Vector) = (1.08,0.94,1.03,0.97)

        [Header(Bleed)]
        _Bleed         ("Bleed Ahead", Range(0,0.2)) = 0.020
        _BleedAlpha    ("Bleed Alpha", Range(0,1)) = 0.09
        _RimWet        ("Wet Rim Width", Range(0,0.2)) = 0.020
        _RimTint       ("Wet Rim Tint", Color) = (0.22,0.23,0.26,1)
        _RimStrength   ("Wet Rim Strength", Range(0,1)) = 0.30
        _FlowSpeed     ("Noise Flow Speed", Range(0,0.6)) = 0.06
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
            float  _Warp;
            float  _Fray;
            float  _Soft;
            float4 _EdgeWeight;

            float  _Bleed;
            float  _BleedAlpha;
            float  _RimWet;
            fixed4 _RimTint;
            float  _RimStrength;
            float  _FlowSpeed;

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

            // 3 倍频 fbm：单层 vnoise 只有"一团团圆斑"，叠加三倍频才有墨渍的层次
            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 3; k++)
                {
                    v += a * vnoise(p);
                    p = p * 2.07 + 13.7;
                    a *= 0.5;
                }
                return v / 0.875;      // 0.5+0.25+0.125 归一化到 0..1
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
                float flow = _Time.y * _FlowSpeed;

                // ① ★ 域扭曲（domain warp）：先用**低频 fbm** 把采样坐标推开。
                //    这是"墨在纸纤维里拐弯、分叉"的关键；只推阈值不扭坐标 ⇒ 边界永远是规则波浪（上一版的问题）。
                float wx = fbm(uv * 1.9 + flow * 0.35) - 0.5;
                float wy = fbm(uv * 1.9 + 3.7 - flow * 0.28) - 0.5;
                float2 wuv = uv + float2(wx, wy) * _Warp;

                // ② 四边异速：在**扭曲后**的坐标上算距离 ⇒ 边界形状跟着一起扭
                float eL = wuv.x        * _EdgeWeight.x;
                float eR = (1.0 - wuv.x) * _EdgeWeight.y;
                float eB = wuv.y        * _EdgeWeight.z;
                float eT = (1.0 - wuv.y) * _EdgeWeight.w;
                float edge = min(min(eL, eR), min(eB, eT));

                // ③ 主形噪声（fbm）+ 细碎裂（高频单层，模拟水渍碎边）
                float n     = fbm(wuv * _NoiseScale + flow);
                float fine  = vnoise(wuv * _NoiseScale * 5.5 - flow * 1.4);
                float wobble = (n - 0.5) * _NoiseStrength + (fine - 0.5) * _Fray;

                float on = step(0.0001, _Progress);
                float front = pow(saturate(_Progress), 1.15) * 0.62;   // 起手慢、中段冲、收笔稳
                float d = front - (edge + wobble);                     // d>0 = 已被墨吃掉

                float cover = smoothstep(-_Soft, _Soft, d) * on;

                // ④ 前渗：前沿外侧一层半透明（宣纸先吸水，墨后到）
                float bleed = saturate(1.0 - abs(d) / max(_Bleed, 1e-4)) * step(d, 0.0001) * on;
                float a = saturate(cover + bleed * _BleedAlpha * (1.0 - cover));

                // ⑤ 湿边：前沿向内一小段偏冷（墨没干透的湿线），Progress 到 1 收掉
                float rim = saturate(1.0 - abs(d) / max(_RimWet, 1e-4)) * on * (1.0 - step(0.999, _Progress));
                float3 col = lerp(_CoverColor.rgb, _RimTint.rgb, saturate(rim * _RimStrength));
                col *= i.color.rgb;

                return fixed4(col, saturate(a * _CoverColor.a * i.color.a));
            }
            ENDCG
        }
    }
    Fallback Off
}
