// ============================================================================
//  万相 · UI 斜俯视（透视）Shader
//  ---------------------------------------------------------------------------
//  2026-10-13 用户："主界面地图太平面了…改成有透视的斜俯视大世界地图；有些效果做不了可以试试 shader"
//
//  做什么：把一张矩形 UI 图当作**地面平面**来显示 ——
//    ① 梯形透视（近宽远窄）：uv.x 按 uv.y 做收缩 ⇒ 看着像往远处收的地面（斜俯视核心）；
//    ② 空气透视（远处叠雾）：越靠上（越远）越向 _HazeColor 靠 ⇒ 拉开纵深、软化远景。
//  ⚠ 这是"keystone（梯形）"近似，不是真正的 3D 投影 —— 对风格化地图足够，且不需要相机。
//
//  ⚠ 与代码侧对齐：C# 侧按同一公式换算"路径/节点"的落点（见 HomeMapJourney.LayoutPerspective）：
//       s = 1 - _Keystone * v ;  屏幕 x = 原始 x * s   （y 不变，v = (y+H/2)/H）
//    改这里务必同步改那边，否则路与节点会错开。
//
//  ⚠ UGUI 自定义 shader 约定（同 UIStreak）：Transparent/IgnoreProjector、Blend SrcAlpha OneMinusSrcAlpha、
//    片元必须乘 i.color（顶点色）与 _Color，否则运行时改 Image.color 无效。
// ============================================================================

Shader "WanXiang/UI/Perspective"
{
    Properties
    {
        _MainTex    ("Texture", 2D) = "white" {}
        _Color      ("Tint", Color) = (1, 1, 1, 1)

        // 透视强度：0 = 原始矩形；0.3 表示最远处横向收到 70%（近宽远窄）
        _Keystone   ("Keystone (0=off)", Range(0, 0.85)) = 0.30

        // 空气透视（雾）：越靠上越向雾色靠
        _HazeColor  ("Haze Color", Color) = (0.93, 0.91, 0.86, 1)
        _Haze       ("Haze Strength", Range(0, 1)) = 0.45
        _HazeStart  ("Haze Start (uv.y)", Range(0, 1)) = 0.30

        // 边缘渐隐：把图四周几成比例的部分渐隐成透明 ⇒ 消掉"硬边矩形/贴画感"，化进背景
        _EdgeFadeY  ("Edge Fade Y", Range(0, 0.5)) = 0.16
        _EdgeFadeX  ("Edge Fade X", Range(0, 0.5)) = 0.05
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
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float  _Keystone;
            fixed4 _HazeColor;
            float  _Haze;
            float  _HazeStart;
            float  _EdgeFadeY;
            float  _EdgeFadeX;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float v = saturate(i.uv.y);                 // 0 = 近（下） · 1 = 远（上）
                float s = 1.0 - _Keystone * v;              // 横向收缩因子（近宽远窄）

                // 反向映射：屏幕上 uv.x 处显示的是原图 0.5+(uv.x-0.5)/s 处的内容 ⇒ 图被"收"成梯形
                float2 uv = float2(0.5 + (i.uv.x - 0.5) / max(s, 0.0001), i.uv.y);

                fixed4 tex = tex2D(_MainTex, uv);
                fixed4 col = tex * i.color * _Color;

                // 空气透视：远处向雾色靠（近处保持原色）
                float hz = smoothstep(_HazeStart, 1.0, v) * _Haze;
                col.rgb = lerp(col.rgb, _HazeColor.rgb, hz);

                // 边缘渐隐：四周成比例渐隐成透明 ⇒ 地图化进背景，不再是一块硬边矩形
                float fy = max(_EdgeFadeY, 0.0001);
                float fx = max(_EdgeFadeX, 0.0001);
                float ey = smoothstep(0.0, fy, v) * smoothstep(0.0, fy, 1.0 - v);
                float ex = smoothstep(0.0, fx, i.uv.x) * smoothstep(0.0, fx, 1.0 - i.uv.x);
                col.a *= ey * ex;

                return col;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
