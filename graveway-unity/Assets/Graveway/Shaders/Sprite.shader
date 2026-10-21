// Camera-facing sprites in one draw call: fire, smoke, dust and sparks
// (vfx.js's SpritePool, with its shader ported line for line). Each copy's
// matrix only carries its position; size, spin and tint come per instance.
// Additive sprites fade to nothing in the fog; blended ones take on the fog
// colour and thin out.
Shader "Graveway/Sprite"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _Additive ("Additive", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Sprite"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Additive;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SizeRot)
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 tint : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 center = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float4 sr = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _SizeRot);
                float c = cos(sr.y), s = sin(sr.y);
                float2 p = float2(v.positionOS.x * c - v.positionOS.y * s, v.positionOS.x * s + v.positionOS.y * c) * sr.x;
                float3 positionVS = TransformWorldToView(center);
                positionVS.xy += p;
                o.positionCS = TransformWViewToHClip(positionVS);
                o.uv = v.uv;
                o.tint = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Tint);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 col = t.rgb * i.tint.rgb;
                half a = t.a * i.tint.a;
                half clear = ComputeFogIntensity(i.fog);      // 1 = no fog
                if (_Additive > 0.5) return half4(col * a * clear, 1);
                return half4(MixFog(col, i.fog), a * (1 - (1 - clear) * 0.6));
            }
            ENDHLSL
        }
    }
}
