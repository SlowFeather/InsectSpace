Shader "InsectSpace/Local AFK Volumetric Fog"
{
    Properties { _NoiseTex("Original fog noise",2D)="gray"{} _NoiseTile("Source noise scale",Float)=50 _DensityScale("Local extinction scale",Float)=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "AfkAtmosphere.hlsl"
            TEXTURE2D(_NoiseTex);SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
            float _NoiseTile,_DensityScale;
            CBUFFER_END
            half4 _StudyFogColor,_StudyFogScatter;
            float4 _StudyFogSettings,_StudyFogLighting;
            struct V { float4 p:SV_POSITION;float2 uv:TEXCOORD0; };
            V vert(uint id:SV_VertexID) { V o;o.p=GetFullScreenTriangleVertexPosition(id);o.uv=GetFullScreenTriangleTexCoord(id);return o; }
            half4 frag(V i):SV_Target
            {
                float raw=SampleSceneDepth(i.uv);
                #if UNITY_REVERSED_Z
                float nearDepth=1;
                #else
                raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
                float nearDepth=UNITY_NEAR_CLIP_VALUE;
                #endif
                float3 endpoint=ComputeWorldSpacePosition(i.uv,raw,UNITY_MATRIX_I_VP);
                float3 origin=unity_OrthoParams.w>.5?ComputeWorldSpacePosition(i.uv,nearDepth,UNITY_MATRIX_I_VP):_WorldSpaceCameraPos;
                float3 ray=endpoint-origin;
                float distance=min(length(ray),120);
                ray=normalize(ray);
                float start=max(_StudyFogSettings.y,0);
                float stepLength=max(distance-start,0)/24;
                float jitter=frac(dot(i.p.xy,float2(.06711056,.00583715)));
                half transmittance=1;
                half3 scattering=0;
                Light sun=GetMainLight();
                float cosine=dot(ray,sun.direction),g=.35;
                float phase=(1-g*g)/pow(max(1+g*g-2*g*cosine,.01),1.5);
                [loop] for(int sample=0;sample<24;sample++) {
                    float3 world=origin+ray*(start+(sample+jitter)*stepLength);
                    float2 noiseUV=(world.xz+world.y*.47)/max(_NoiseTile,.1)+_Time.y*float2(-.001,.001);
                    half noise=SAMPLE_TEXTURE2D_LOD(_NoiseTex,sampler_NoiseTex,noiseUV,0).r;
                    float height=exp(-max(world.y-_StudyFogSettings.z,0)*_StudyFogSettings.w);
                    float density=max(_StudyFogSettings.x,0)*_DensityScale*height*lerp(.45,1.4,noise);
                    half opacity=1-exp(-density*stepLength);
                    Light light=GetMainLight(TransformWorldToShadowCoord(world));
                    half visibility=light.shadowAttenuation*StudyCloudShadow(world);
                    half3 color=_StudyFogColor.rgb+_StudyFogScatter.rgb*(.25+visibility*phase)*(_StudyFogLighting.x*8+.06);
                    color+=light.color*visibility*phase*_StudyFogLighting.y*.05;
                    scattering+=transmittance*opacity*color;
                    transmittance*=1-opacity;
                }
                return half4(scattering,1-transmittance);
            }
            ENDHLSL
        }
    }
}
