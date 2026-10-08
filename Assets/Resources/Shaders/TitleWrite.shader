// ============================================================================
//  万相 · 标题「书写」shader
//  ---------------------------------------------------------------------------
//  把一个字的显现做成**从左到右的笔锋推进**，而不是整字弹出：
//    · `_Reveal` 0→1 控制"笔尖走到哪" —— 笔尖左侧显示、右侧还是空的；
//    · 笔尖处有一段**柔和且带锯齿的过渡**（不是硬切），像毛笔头扫过；
//    · 已经写过的部分保留一点**墨色浓淡变化**（左端墨重、笔尖处墨新）。
//  用法：每个字一个 TMP 对象 + 各自一份材质（只 `_Reveal` 不同），由 C# 逐字推进。
//
//  ⚠ 放 Assets/Resources/Shaders/ 下：`BattleStage2D`/面板都是运行时取，Inspector 存不住引用；
//     且 Resources 能保证进包。
//  ⚠ 与 TMP 搭配：TMP 的图集采样走 `_MainTex`，颜色走顶点色（v.color），这里只做 mask 乘算。
// ============================================================================
Shader "WanXiang/TitleWrite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Atlas", 2D) = "white" {}
        _Reveal  ("Reveal (0=未写 1=写完)", Range(0, 1)) = 1
        _Soft    ("笔锋柔和度", Range(0.001, 0.5)) = 0.08
        _Rough   ("笔锋锯齿幅度", Range(0, 0.2)) = 0.045
        _InkEdge ("笔尖新墨加深", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };
            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Reveal, _Soft, _Rough, _InkEdge;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;

                // 笔尖位置：从左（0）走到右（>1 表示已越过整个字）
                float tip = _Reveal * (1.0 + _Soft * 2.0) - _Soft;

                // 笔锋线做一点纵向锯齿 —— 直上直下的切边太像"擦除"，锯齿才像毛笔头
                float jag = sin(i.uv.y * 37.0) * 0.5 + sin(i.uv.y * 91.0 + 1.7) * 0.5;
                float edge = tip + jag * _Rough;

                // 用 smoothstep 做柔和笔锋（而不是 step 硬切）
                float m = smoothstep(edge - _Soft, edge + _Soft, i.uv.x);   // 0 = 已写, 1 = 未写
                c.a *= (1.0 - m);

                // 笔尖那一段墨色更重一点，像刚下笔的湿墨
                float newInk = 1.0 - smoothstep(0.0, _Soft * 3.0, abs(i.uv.x - edge));
                c.rgb *= (1.0 - _InkEdge * newInk);

                clip(c.a - 0.002);
                return c;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
