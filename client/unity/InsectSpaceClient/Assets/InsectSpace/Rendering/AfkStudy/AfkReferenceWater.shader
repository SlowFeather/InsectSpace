Shader "InsectSpace/Local Reference Water"
{
    Properties
    {
        _BaseColor("Water",Color)=(.20,.39,.46,1)
        _EdgeColor("Shore",Color)=(.58,.72,.69,1)
        _Rain("Rain ripples",Range(0,1))=0
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Pass
        {
            Tags{"LightMode"="UniversalForward"}
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor,_EdgeColor;float _Rain;
            CBUFFER_END
            struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
            struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;float2 uv:TEXCOORD1;half fog:TEXCOORD2;};
            V vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.uv=a.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            half4 frag(V i):SV_Target
            {
                float waves=sin(i.w.x*2.2+i.w.z*1.4+_Time.y*.7)*sin(i.w.z*3.9-_Time.y*.4);
                half3 color=_BaseColor.rgb*(.97+waves*.035);
                float edge=1-smoothstep(.018,.11,i.uv.x);
                color=lerp(color,_EdgeColor.rgb,edge*.72);
                float2 cell=floor(i.w.xz*1.3),local=frac(i.w.xz*1.3)-.5;
                float phase=frac(_Time.y*.65+hash(cell));
                float ring=(1-smoothstep(.012,.026,abs(length(local)-phase*.42)))*(1-phase)*step(.7,hash(cell+21));
                color+=ring*.035*_Rain;
                float glint=pow(saturate(waves),24)*.045;
                color+=glint;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
