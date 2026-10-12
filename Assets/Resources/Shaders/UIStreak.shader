// ============================================================================
//  万相 · UI 拖尾 /  streaks Shader
//  ---------------------------------------------------------------------------
//  2026-10-12 用户："你这个横扫转场能不能让这些粒子带一些尾迹啊"
//             "我发现你很少写 shader，为了好效果，你可以多写一些 shader，
//              自己生成需要的贴图"
//
//  用途：给横扫转场的粒子（以及后续任何需要"速度感/拖尾"的 UI 元素）做尾迹。
//  一张贴图 + 这个 shader ⇒ 头部实、尾部渐隐的拖尾，不需要逐粒子两张图对位。
//
//  原理：按 uv.x 分段 —— uv.x > _Head 的部分正常显示（头），
//        uv.x < _Head 的部分按 pow 曲线渐隐（尾），并整体乘 _StreakAlpha。
// ============================================================================

Shader "WanXiang/UI/Streak"
{
    Properties
    {
        _MainTex      ("Texture", 2D) = "white" {}
        _Color        ("Color", Color) = (1, 1, 1, 1)
        // 头部占比：uv.x 大于它算"头"（不渐隐），小于它算"尾"（渐隐）
        _Head         ("Head Ratio (0..1)", Range(0.05, 0.95)) = 0.35
        // 尾迹渐隐曲线指数：越大尾越短促
        _FadePow      ("Fade Power", Range(0.5, 6)) = 1.8
        // 整体不透明度
        _StreakAlpha  ("Streak Alpha", Range(0, 1)) = 0.9
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
            float  _Head;
            float  _FadePow;
            float  _StreakAlpha;

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
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 col = tex * i.color * _Color;

                // 头部：完全保留；尾部：按 pow 渐隐
                float isHead = step(_Head, i.uv.x);
                float tail = pow(saturate(i.uv.x / max(_Head, 0.0001)), _FadePow);
                col.a *= lerp(tail, 1.0, isHead);
                col.a *= _StreakAlpha;

                // 尾部略微提亮（速度感）：越靠尾越偏白/偏亮
                col.rgb = lerp(col.rgb, min(col.rgb * 1.35 + 0.10, 1.0), 1.0 - isHead);

                return col;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
