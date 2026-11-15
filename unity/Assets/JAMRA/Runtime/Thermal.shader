// جَمرة — عرض حراري للوحة كاميرا الدرون
// تحويل تقريبي من الصورة المرئية إلى سلّم حراري: السطوع يمثّل حرارة السطح
// (التربة المشمسة أدفأ من المظلّة)، واللهب يُدفع إلى أعلى السلّم.
Shader "Jamra/Thermal"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }
            fixed3 ramp (float t)
            {
                fixed3 c0 = fixed3(0.02, 0.02, 0.16);
                fixed3 c1 = fixed3(0.42, 0.02, 0.55);
                fixed3 c2 = fixed3(0.93, 0.26, 0.10);
                fixed3 c3 = fixed3(1.00, 0.84, 0.20);
                fixed3 c4 = fixed3(1.00, 1.00, 1.00);
                if (t < 0.25) return lerp(c0, c1, t / 0.25);
                if (t < 0.50) return lerp(c1, c2, (t - 0.25) / 0.25);
                if (t < 0.78) return lerp(c2, c3, (t - 0.50) / 0.28);
                return lerp(c3, c4, (t - 0.78) / 0.22);
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 c = tex2D(_MainTex, i.uv).rgb;
                float lum = dot(c, float3(0.3, 0.59, 0.11));
                float fire = smoothstep(0.42, 0.78, c.r - c.b) * c.r;
                float heat = saturate(lum * 0.50 + fire * 0.62);
                return fixed4(ramp(heat), 1);
            }
            ENDCG
        }
    }
}
