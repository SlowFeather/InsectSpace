Shader "InsectSpace/Local Fairy Glow"
{
    Properties
    {
        [HDR] _Color("Fairy glow",Color)=(2.8,1.9,.65,1)
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent"}
        Cull Off ZWrite Off Blend One One
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};
            struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
            V vert(A a)
            {
                V o;
                float3 center=TransformObjectToWorld(float3(0,0,0));
                float size=length(unity_ObjectToWorld._m00_m10_m20);
                float3 position=center+UNITY_MATRIX_I_V._m00_m10_m20*a.p.x*size+UNITY_MATRIX_I_V._m01_m11_m21*a.p.y*size;
                o.p=TransformWorldToHClip(position);o.uv=a.uv*2-1;return o;
            }
            half4 frag(V i):SV_Target
            {
                float radius=dot(i.uv,i.uv);
                float core=exp(-radius*75);
                float halo=exp(-radius*7)*.18*saturate(1-radius);
                float wing=exp(-dot(float2(abs(i.uv.x)-.3,i.uv.y-.1)*float2(5,13),float2(abs(i.uv.x)-.3,i.uv.y-.1)*float2(5,13)))*.28;
                return half4(_Color.rgb*(core+halo+wing),1);
            }
            ENDHLSL
        }
    }
}
