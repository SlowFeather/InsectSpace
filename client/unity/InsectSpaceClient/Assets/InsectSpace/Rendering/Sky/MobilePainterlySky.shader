Shader "InsectSpace/Mobile Painterly Sky"
{
    Properties
    {
        [NoScaleOffset] _CloudAtlas ("Cloud atlas (R shape, G lighting, B wisps)", 2D) = "black" {}
        _Zenith ("Zenith", Color) = (0.045,0.33,0.65,1)
        _Middle ("Middle sky", Color) = (0.18,0.55,0.77,1)
        _Horizon ("Horizon haze", Color) = (0.64,0.83,0.86,1)
        _CloudLight ("Cloud light", Color) = (0.95,0.98,0.91,1)
        _CloudShade ("Cloud shade", Color) = (0.48,0.73,0.80,1)
        _SunColor ("Celestial light", Color) = (1,0.89,0.62,1)
        _SunDirection ("Sun direction", Vector) = (0.6,0.5,0.6,0)
        _CloudOffset ("Cloud motion", Vector) = (0,0,0,0)
        _Rotation ("Azimuth radians", Float) = 0
        _Exposure ("Exposure", Range(0,3)) = 1
        _Coverage ("Cloud opacity", Range(0,1.5)) = 1
        _Night ("Night blend", Range(0,1)) = 0
        _SunSize ("Celestial angular radius", Range(0.005,0.1)) = 0.022
        [Toggle(_SKY_DETAILS)] _Details ("High clouds and stars", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_local _ _SKY_DETAILS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            TEXTURE2D(_CloudAtlas); SAMPLER(sampler_CloudAtlas);
            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Middle, _Horizon, _CloudLight, _CloudShade, _SunColor;
                float4 _SunDirection, _CloudOffset;
                float _Rotation;
                half _Exposure, _Coverage, _Night, _SunSize, _Details;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float sn,cs; sincos(_Rotation,sn,cs);
                output.direction = float3(cs*input.positionOS.x-sn*input.positionOS.z,input.positionOS.y,sn*input.positionOS.x+cs*input.positionOS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d = normalize(input.direction);
                float2 uv = float2(atan2(d.x,d.z)*0.159154943+0.5,asin(clamp(d.y,-1.0,1.0))*0.318309886+0.5);
                half altitude = saturate(d.y);
                half3 sky = lerp(_Horizon.rgb,_Middle.rgb,smoothstep(0.0,0.28,altitude));
                sky = lerp(sky,_Zenith.rgb,smoothstep(0.22,0.92,altitude));
                float3 sunDir = normalize(_SunDirection.xyz);
                float sunDistance = length(d-sunDir);
                half halo = pow(saturate(dot(d,sunDir)),48.0)*0.12;
                half disk = 1.0-smoothstep(_SunSize*0.88,_SunSize,sunDistance);
                float3 moonCutDir = normalize(sunDir+float3(_SunSize*0.65,_SunSize*0.24,0));
                half cut = smoothstep(_SunSize*0.77,_SunSize*0.84,length(d-moonCutDir));
                sky += _SunColor.rgb*(halo+disk*lerp(0.9,cut,_Night));
                #if defined(_SKY_DETAILS)
                    float2 starUv = uv*float2(570,240);
                    float2 cell = floor(starUv);
                    float starRandom = frac(sin(dot(cell,float2(127.1,311.7)))*43758.5453);
                    float starRadius = length(frac(starUv)-0.5);
                    half star = (1.0-smoothstep(0.025,0.17,starRadius))*step(0.994,starRandom);
                    sky += star*_Night*smoothstep(0.04,0.25,altitude)*0.85;
                    half wisps = SAMPLE_TEXTURE2D(_CloudAtlas,sampler_CloudAtlas,uv+_CloudOffset.zw).b;
                    sky = lerp(sky,_CloudLight.rgb,wisps*0.42*_Coverage);
                #endif
                half2 cloud = SAMPLE_TEXTURE2D(_CloudAtlas,sampler_CloudAtlas,uv+_CloudOffset.xy).rg;
                half cloudAlpha = saturate(cloud.r*_Coverage)*smoothstep(-0.065,0.02,d.y);
                half3 cloudColor = lerp(_CloudShade.rgb,_CloudLight.rgb,cloud.g);
                cloudColor = lerp(_Horizon.rgb,cloudColor,smoothstep(-0.01,0.13,d.y));
                sky = lerp(sky,cloudColor,cloudAlpha);
                return half4(max(0,sky)*_Exposure,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
