// Many copies of one mesh in one draw call, each with its own colour: coins,
// tracers, blood and pickup bits, grenades and debris (the web version's
// InstancedMeshes). Lit per vertex by the moon and the sky light, like
// three's Lambert, or unlit; opaque or additive. All of that is set on the
// material, not with keywords, so a phone build can't strip any of it.
Shader "Graveway/Instanced"
{
    Properties
    {
        _Lit ("Lit", Float) = 1
        _Additive ("Additive", Float) = 0
        _Alpha ("Alpha", Float) = 1
        _Emission ("Emission", Color) = (0, 0, 0, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("Z write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "Instanced"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Lit;
                half _Additive;
                half _Alpha;
                half4 _Emission;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : TEXCOORD0;
                half fog : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                half3 c = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;
                if (_Lit > 0.5)
                {
                    half3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                    Light main = GetMainLight();
                    c *= main.color * saturate(dot(n, main.direction)) + SampleSH(n);
                }
                o.color = c + _Emission.rgb;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // Glowing things fade to nothing in the fog; solid ones to the fog colour.
                if (_Additive > 0.5) return half4(MixFogColor(i.color, half3(0, 0, 0), i.fog) * _Alpha, 1);
                return half4(MixFog(i.color, i.fog), _Alpha);
            }
            ENDHLSL
        }
    }
}
