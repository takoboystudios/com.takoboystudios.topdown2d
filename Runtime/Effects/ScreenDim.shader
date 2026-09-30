// The Brand's dimmed screen (ScreenDim, T-200). Colour: an ordinary blend toward the dim's colour.
// Alpha: the dim's own opacity replaces what was there, so every pixel it covers carries a value
// below 1. Anything opaque drawn over it afterwards (the one who froze the world, their effects, the
// HUD) writes 1 back. A game's full-screen colour pass can read that alpha to treat the dimmed world
// and what stands in front of it differently: Hell Wilds' palette clamp remaps only the dimmed part.
Shader "Hidden/TopDown2D/ScreenDim"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha, One Zero

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
    Fallback Off
}
