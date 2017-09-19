Shader "Hidden/SEGIRenderSunDepth_C" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_MainTex ("Base (RGB)", 2D) = "white" {}
	_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.333
}
SubShader 
{
	Pass
	{
	
		CGPROGRAM
			
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0
			
			#include "UnityCG.cginc"
			
			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			float _Cutoff;
			
			
			struct v2f
			{
				float4 pos : SV_POSITION;
				float4 uv : TEXCOORD0;
				float3 normal : TEXCOORD1;
				half4 color : COLOR;
			};
			
			
			v2f vert (appdata_full v)
			{
				v2f o;
				
				o.pos = UnityObjectToClipPos(v.vertex);
				
				float3 pos = o.pos;
				
				o.pos.xy = (o.pos.xy);
				
				
				o.uv = float4(TRANSFORM_TEX(v.texcoord.xy, _MainTex), 1.0, 1.0);
				o.normal = UnityObjectToWorldNormal(v.normal);
				
				o.color = v.color;
				
				return o;
			}
			
			
			sampler2D GILightCookie;
			float4x4 GIProjection;
			
			float4 frag (v2f input) : SV_Target
			{
				float depth = input.pos.z;
				
				return depth;
			}
			
		ENDCG
	}
}

Fallback "Legacy Shaders/VertexLit"
}
