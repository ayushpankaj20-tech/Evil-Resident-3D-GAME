Shader "Game/DuskSkybox"
{
    Properties
    {
        _Top ("Top", Color) = (0.03, 0.05, 0.13, 1)
        _Mid ("Mid", Color) = (0.20, 0.19, 0.34, 1)
        _Horizon ("Horizon Glow", Color) = (0.95, 0.47, 0.26, 1)
        _HorizonDim ("Horizon Dim", Color) = (0.28, 0.24, 0.36, 1)
        _Ground ("Ground", Color) = (0.05, 0.05, 0.07, 1)
        _GlowDir ("Glow Direction", Vector) = (-0.2, 0, 1, 0)
        _MoonDir ("Moon Direction", Vector) = (0.7, 0.5, 0.2, 0)
        _MoonColor ("Moon Color", Color) = (1, 0.96, 0.86, 1)
        _MoonSize ("Moon Size", Range(0.005, 0.2)) = 0.045
        _Stars ("Star Amount", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Top, _Mid, _Horizon, _HorizonDim, _Ground, _MoonColor;
            float4 _GlowDir, _MoonDir;
            float _MoonSize, _Stars;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;

                float2 gd = normalize(_GlowDir.xz);
                float2 hd = normalize(d.xz + 1e-5);
                float g = pow(saturate(dot(hd, gd) * 0.5 + 0.5), 3.0);
                float3 horizon = lerp(_HorizonDim.rgb, _Horizon.rgb, g);

                float3 col = lerp(horizon, _Mid.rgb, smoothstep(0.0, 0.28, y));
                col = lerp(col, _Top.rgb, smoothstep(0.18, 0.8, y));
                col = lerp(col, _Ground.rgb, smoothstep(0.0, -0.12, y));

                float3 md = normalize(_MoonDir.xyz);
                float mdot = dot(d, md);
                float moon = smoothstep(cos(_MoonSize), cos(_MoonSize * 0.85), mdot);
                float halo = pow(saturate(mdot), 220.0) * 0.35 + pow(saturate(mdot), 24.0) * 0.08;
                col += _MoonColor.rgb * (moon + halo);

                float3 cell = floor(d * 140.0);
                float s = step(1.0 - 0.004 * _Stars, hash(cell));
                col += s * smoothstep(0.08, 0.5, y) * (1.0 - moon) * 0.8;

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
