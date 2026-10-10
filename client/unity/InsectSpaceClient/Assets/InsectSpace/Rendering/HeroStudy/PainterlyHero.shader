Shader "InsectSpace/Painterly Hero"
{
    Properties
    {
        _BaseMap("Albedo",2D)="white" {}
        _BaseColor("Tint",Color)=(1,1,1,1)
        _ShadowTint("Cool shadow",Color)=(.57,.68,.77,1)
        _PaletteShift("Silver and navy palette",Range(0,1))=1
        _Softness("Skin light softness",Range(0,1))=0
    }
    SubShader
    {
        Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
        Cull Off
        Pass
        {
            Name "PainterlyCharacter" Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;half4 _BaseColor,_ShadowTint;half _PaletteShift,_Softness;
            CBUFFER_END
            struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};
            struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 n:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;};
            V Vert(A a) {V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(a.n);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 Frag(V i):SV_Target
            {
                half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                half luma=dot(albedo,half3(.2126,.7152,.0722));
                half red=smoothstep(.20,.48,(albedo.r-max(albedo.g,albedo.b))/max(albedo.r,.015));
                half gold=saturate((albedo.r-albedo.b)*3)*saturate((albedo.g-albedo.b)*5);
                albedo=lerp(albedo,luma*half3(.44,.80,1.42),red*_PaletteShift);
                albedo=lerp(albedo,luma*half3(1.14,1.06,.83),gold*_PaletteShift*.18);
                half3 n=normalize(i.n);Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
                half ndl=dot(n,sun.direction);half band=smoothstep(-.12,.10,ndl)*.65+smoothstep(.53,.65,ndl)*.35;
                band=lerp(band,smoothstep(-.35,.85,ndl),_Softness);
                half shadow=lerp(.74,1,sun.shadowAttenuation);
                half3 lit=lerp(_ShadowTint.rgb,half3(1.12,1.08,.97),band)*shadow;
                half3 env=max(half3(.17,.20,.26),sun.color*.65+SampleSH(n)*.60);
                half rim=pow(1-saturate(dot(n,normalize(GetWorldSpaceViewDir(i.world)))),3)*.10*saturate(ndl+.3);
                return half4(MixFog(albedo*lit*env+rim*half3(.68,.79,.82),i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
