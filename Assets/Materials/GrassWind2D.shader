Shader "Custom/GrassWind2D"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color     ("Tint", Color) = (1,1,1,1)
        _Strength  ("Wind Strength", Range(0, 0.2)) = 0.03   // насколько сильно гнётся
        _Speed     ("Wind Speed", Range(0, 10))     = 2      // скорость качания
        _Frequency ("Wave Frequency", Range(0, 50)) = 10     // насколько "рвано" идёт волна по колоскам
        _Gust      ("Gust", Range(0, 1))            = 0.5    // медленные порывы поверх
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        ZWrite Off
        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _Strength, _Speed, _Frequency, _Gust;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
                float  worldX : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                o.color  = v.color * _Color;
                // мировая X-позиция — чтобы соседние копии спрайта качались не синхронно
                o.worldX = mul(unity_ObjectToWorld, v.vertex).x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // низ (uv.y = 0) стоит, верх гнётся сильнее всего
                float bend = uv.y * uv.y;

                // основная волна + медленный порыв
                float t    = _Time.y * _Speed;
                float wave = sin(t + uv.x * _Frequency + i.worldX)
                           + sin(t * 0.37 + i.worldX * 0.5) * _Gust;

                uv.x += wave * _Strength * bend;

                fixed4 col = tex2D(_MainTex, uv) * i.color;
                return col;
            }
            ENDCG
        }
    }
}
