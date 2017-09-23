Shader "Hidden/SEGIVoxelizeScene" {
	Properties
	{
		_Color ("Main Color", Color) = (1,1,1,1)
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_EmissionColor("Color", Color) = (0,0,0)
		_EmissionMap("Emission", 2D) = "white" {}
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.333
		_BlockerValue ("Blocker Value", Range(0, 10)) = 0
	}
	SubShader 
	{
		Cull Off
		ZTest Always
		
		Pass
		{
			CGPROGRAM
			
				#pragma target 5.0
				#pragma vertex vert
				#pragma fragment frag
				#pragma geometry geom
				#include "UnityCG.cginc"
				
				RWTexture3D<uint> RG0;
				
				int LayerToVisualize;
				
				float4x4 SEGIVoxelViewFront;
				float4x4 SEGIVoxelViewLeft;
				float4x4 SEGIVoxelViewTop;
				
				sampler2D _MainTex;
				sampler2D _EmissionMap;
				float _Cutoff;
				float4 _MainTex_ST;
				half4 _EmissionColor;

				float SEGISecondaryBounceGain;
				
				float _BlockerValue;
				
				
				struct v2g
				{
					float4 pos : SV_POSITION;
					half4 uv : TEXCOORD0;
					float3 normal : TEXCOORD1;
					float angle : TEXCOORD2;
				};
				
				struct g2f
				{
					float4 pos : SV_POSITION;
					half4 uv : TEXCOORD0;
					float3 normal : TEXCOORD1;
					float angle : TEXCOORD2;
				};
				
				half4 _Color;
				
				v2g vert(appdata_full v)
				{
					v2g o;
					UNITY_INITIALIZE_OUTPUT(v2g, o);
					
					float4 vertex = v.vertex;
					
					o.normal = UnityObjectToWorldNormal(v.normal);
					float3 absNormal = abs(o.normal);
					
					o.pos = vertex;
					
					o.uv = float4(TRANSFORM_TEX(v.texcoord.xy, _MainTex), 1.0, 1.0);
					
					
					return o;
				}
				
				int SEGIVoxelResolution;
				
				[maxvertexcount(3)]
				void geom(triangle v2g input[3], inout TriangleStream<g2f> triStream)
				{
					v2g p[3];
					int i = 0;
					for (i = 0; i < 3; i++)
					{
						p[i] = input[i];
						p[i].pos = mul(unity_ObjectToWorld, p[i].pos);						
					}
					

					float3 realNormal = float3(0.0, 0.0, 0.0);
					
					float3 V = p[1].pos.xyz - p[0].pos.xyz;
					float3 W = p[2].pos.xyz - p[0].pos.xyz;
					
					realNormal.x = (V.y * W.z) - (V.z * W.y);
					realNormal.y = (V.z * W.x) - (V.x * W.z);
					realNormal.z = (V.x * W.y) - (V.y * W.x);
					
					float3 absNormal = abs(realNormal);
					

					
					int angle = 0;
					if (absNormal.z > absNormal.y && absNormal.z > absNormal.x)
					{
						angle = 0;
					}
					else if (absNormal.x > absNormal.y && absNormal.x > absNormal.z)
					{
						angle = 1;
					}
					else if (absNormal.y > absNormal.x && absNormal.y > absNormal.z)
					{
						angle = 2;
					}
					else
					{
						angle = 0;
					}
					
					for (i = 0; i < 3; i ++)
					{
						///*
						if (angle == 0)
						{
							p[i].pos = mul(SEGIVoxelViewFront, p[i].pos);					
						}
						else if (angle == 1)
						{
							p[i].pos = mul(SEGIVoxelViewLeft, p[i].pos);					
						}
						else
						{
							p[i].pos = mul(SEGIVoxelViewTop, p[i].pos);		
						}
						
						p[i].pos = mul(UNITY_MATRIX_P, p[i].pos);
						
						#if defined(UNITY_REVERSED_Z)
						p[i].pos.z = 1.0 - p[i].pos.z;	
						#else 
						p[i].pos.z *= -1.0;	
						#endif
						
						p[i].angle = (float)angle;
					}
					
					triStream.Append(p[0]);
					triStream.Append(p[1]);
					triStream.Append(p[2]);
				}

				float3 rgb2hsv(float3 c)
				{
					float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
					float4 p = lerp(float4(c.bg, k.wz), float4(c.gb, k.xy), step(c.b, c.g));
					float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

					float d = q.x - min(q.w, q.y);
					float e = 1.0e-10;

					return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
				}

				float3 hsv2rgb(float3 c)
				{
					float4 k = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
					float3 p = abs(frac(c.xxx + k.xyz) * 6.0 - k.www);
					return c.z * lerp(k.xxx, saturate(p - k.xxx), c.y);
				}

				float4 DecodeRGBAuint(uint value)
				{
					uint ai = value & 0x0000007F;
					uint vi = (value / 0x00000080) & 0x000007FF;
					uint si = (value / 0x00040000) & 0x0000007F;
					uint hi = value / 0x02000000;

					float h = float(hi) / 127.0;
					float s = float(si) / 127.0;
					float v = (float(vi) / 2047.0) * 10.0;
					float a = ai * 2.0;

					v = pow(v, 3.0);

					float3 color = hsv2rgb(float3(h, s, v));

					return float4(color.rgb, a);
				}

				uint EncodeRGBAuint(float4 color)
				{
					//7[HHHHHHH] 7[SSSSSSS] 11[VVVVVVVVVVV] 7[AAAAAAAA]
					float3 hsv = rgb2hsv(color.rgb);
					hsv.z = pow(hsv.z, 1.0 / 3.0);

					uint result = 0;

					uint a = min(127, uint(color.a / 2.0));
					uint v = min(2047, uint((hsv.z / 10.0) * 2047));
					uint s = uint(hsv.y * 127);
					uint h = uint(hsv.x * 127);

					result += a;
					result += v * 0x00000080; // << 7
					result += s * 0x00040000; // << 18
					result += h * 0x02000000; // << 25

					return result;
				}

				void interlockedAddFloat4(RWTexture3D<uint> destination, int3 coord, float4 value)
				{
					uint writeValue = EncodeRGBAuint(value);
					uint compareValue = 0;
					uint originalValue;

					[allow_uav_condition]
					while (true)
					{
						InterlockedCompareExchange(destination[coord], compareValue, writeValue, originalValue);
						if (compareValue == originalValue)
							break;
						compareValue = originalValue;
						float4 originalValueFloats = DecodeRGBAuint(originalValue);
						writeValue = EncodeRGBAuint(originalValueFloats + value);
					}
				}

				void interlockedAddFloat4b(RWTexture3D<uint> destination, int3 coord, float4 value)
				{
					uint writeValue = EncodeRGBAuint(value);
					uint compareValue = 0;
					uint originalValue;

					[allow_uav_condition]
					while (true)
					{
						InterlockedCompareExchange(destination[coord], compareValue, writeValue, originalValue);
						if (compareValue == originalValue)
							break;
						compareValue = originalValue;
						float4 originalValueFloats = DecodeRGBAuint(originalValue);
						writeValue = EncodeRGBAuint(originalValueFloats + value);
					}
				}

				float4x4 SEGIVoxelToGIProjection;
				float4x4 SEGIVoxelProjectionInverse;
				sampler2D SEGISunDepth;
				float4 SEGISunlightVector;
				float4 GISunColor;
				float4 SEGIVoxelSpaceOriginDelta;
				
				sampler3D SEGIVolumeTexture1;

				int SEGIInnerOcclusionLayers;
				
				#define VoxelResolution (SEGIVoxelResolution * (1 + SEGIVoxelAA))

				int SEGIVoxelAA;
				
				float4 frag (g2f input) : SV_TARGET
				{
					int3 coord = int3((int)(input.pos.x), (int)(input.pos.y), (int)(input.pos.z * VoxelResolution));
					
					float3 absNormal = abs(input.normal);
					
					int angle = 0;
					
					angle = (int)input.angle;

					if (angle == 1)
					{
						coord.xyz = coord.zyx;
						coord.z = VoxelResolution - coord.z - 1;
					}
					else if (angle == 2)
					{
						coord.xyz = coord.xzy;
						coord.y = VoxelResolution - coord.y - 1;
					}
					
					float3 fcoord = (float3)(coord.xyz) / VoxelResolution;

					float4 shadowPos = mul(SEGIVoxelProjectionInverse, float4(fcoord * 2.0 - 1.0, 0.0));
					shadowPos = mul(SEGIVoxelToGIProjection, shadowPos);
					shadowPos.xyz = shadowPos.xyz * 0.5 + 0.5;
					
					float sunDepth = tex2Dlod(SEGISunDepth, float4(shadowPos.xy, 0, 0)).x;
					#if defined(UNITY_REVERSED_Z)
					sunDepth = 1.0 - sunDepth;
					#endif
					
					float sunVisibility = saturate((sunDepth - shadowPos.z + 0.2525) * 1000.0);


					float sunNdotL = saturate(dot(input.normal, -SEGISunlightVector.xyz));
					
					float4 tex = tex2D(_MainTex, input.uv.xy);
					float4 emissionTex = tex2D(_EmissionMap, input.uv.xy);
					
					float4 color = _Color;

					if (length(_Color.rgb) < 0.0001)
					{
						color.rgb = float3(1, 1, 1);
					}

					
					float3 col = sunVisibility.xxx * sunNdotL * color.rgb * tex.rgb * GISunColor.rgb * GISunColor.a + _EmissionColor.rgb * 0.9 * emissionTex.rgb;

					float4 prevBounce = tex3D(SEGIVolumeTexture1, fcoord + SEGIVoxelSpaceOriginDelta.xyz);
					col.rgb += prevBounce.rgb * 1.6 * SEGISecondaryBounceGain * tex.rgb * color.rgb;
					 
					float4 result = float4(col.rgb, 2.0);

					
					const float sqrt2 = sqrt(2.0) * 1.0;
					
					coord /= (uint)SEGIVoxelAA + 1u;


					if (_BlockerValue > 0.01)
					{
						result.a += 20.0;
						result.a += _BlockerValue;
						result.rgb = float3(0.0, 0.0, 0.0);
					}

					interlockedAddFloat4(RG0, coord, result);

					if (SEGIInnerOcclusionLayers > 0)
					{
						interlockedAddFloat4b(RG0, coord - int3((int)(input.normal.x * sqrt2 * 1.0), (int)(input.normal.y * sqrt2 * 1.0), (int)(input.normal.z * sqrt2 * 1.0)), float4(0.0, 0.0, 0.0, 8.0));
					}

					if (SEGIInnerOcclusionLayers > 1)
					{
						interlockedAddFloat4b(RG0, coord - int3((int)(input.normal.x * sqrt2 * 2.0), (int)(input.normal.y * sqrt2 * 2.0), (int)(input.normal.z * sqrt2 * 2.0)), float4(0.0, 0.0, 0.0, 22.0));
					}
					
					return float4(0.0, 0.0, 0.0, 0.0);
				}
			
			ENDCG
		}
	} 
	FallBack Off
}
