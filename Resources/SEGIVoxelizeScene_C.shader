Shader "Hidden/SEGIVoxelizeScene_C" {
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
				#include "SEGIUtils.cginc"
				#include "SEGIShaderCommon.cginc"

				RWTexture3D<uint> RG0;
				
				int LayerToVisualize;
				
				float4x4 SEGIVoxelViewFront;
				float4x4 SEGIVoxelViewLeft;
				float4x4 SEGIVoxelViewTop;

				half4 _Color;
				sampler2D _EmissionMap;
				float _Cutoff;
				half4 _EmissionColor;

				float SEGISecondaryBounceGain;
				
				float _BlockerValue;

				int SEGIVoxelResolution;
				int SEGIVoxelAA;
				#define VoxelResolution (SEGIVoxelResolution * (1 + SEGIVoxelAA))

				float4x4 SEGIVoxelVPFront;
				float4x4 SEGIVoxelVPLeft;
				float4x4 SEGIVoxelVPTop;
				
				[maxvertexcount(3)]
				void geom(triangle v2g input[3], inout TriangleStream<g2f> triStream)
				{
					v2g p[3];
					int i = 0;
					for (i = 0; i < 3; i++)
					{
						p[i] = input[i];
						p[i].pos = UnityObjectToClipPos(p[i].pos);	
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
						float3 op = p[i].pos.xyz * float3(1.0, 1.0, 1.0);
						op.z = op.z * 2.0 - 1.0;

						if (angle == 0)
						{
							p[i].pos.xyz = op.xyz;	
						}
						else if (angle == 1)
						{
							p[i].pos.xyz = op.zyx * float3(1.0, 1.0, -1.0);
						}
						else
						{
							p[i].pos.xyz = op.xzy * float3(1.0, 1.0, -1.0);
						}

						p[i].pos.z = p[i].pos.z * 0.5 + 0.5;
						
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

				float4x4 SEGIVoxelToGIProjection;
				float4x4 SEGIVoxelProjectionInverse;
				sampler2D SEGISunDepth;
				float4 SEGISunlightVector;
				float4 GISunColor;
				float4 SEGIVoxelSpaceOriginDelta;
				
				sampler3D SEGICurrentIrradianceVolume;
				int SEGIInnerOcclusionLayers;

				


				float4 SEGIClipmapOverlap;
				
				float4 frag (g2f input) : SV_TARGET
				{
					int3 coord = int3((int)(input.pos.x), (int)(input.pos.y), (int)(input.pos.z * VoxelResolution));
					
					float3 absNormal = abs(input.normal);
					
					int angle = 0;
					
					angle = (int)round(input.angle);
					
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

					float3 minCoord = (SEGIClipmapOverlap.xyz * 1.0 + 0.5) - SEGIClipmapOverlap.w * 0.5;
					minCoord += 16.0 / VoxelResolution;
					float3 maxCoord = (SEGIClipmapOverlap.xyz * 1.0 + 0.5) + SEGIClipmapOverlap.w * 0.5;
					maxCoord -= 16.0 / VoxelResolution;


					if (fcoord.x > minCoord.x && fcoord.x < maxCoord.x &&
						fcoord.y > minCoord.y && fcoord.y < maxCoord.y &&
						fcoord.z > minCoord.z && fcoord.z < maxCoord.z)
					{
						discard;
					}


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
					else
					{
						color.rgb *= color.a;
					}

					
					float3 col = sunVisibility.xxx * sunNdotL * color.rgb * tex.rgb * GISunColor.rgb * GISunColor.a + _EmissionColor.rgb * 0.9 * emissionTex.rgb;

					float4 prevBounce = tex3D(SEGICurrentIrradianceVolume, fcoord + SEGIVoxelSpaceOriginDelta.xyz);
					col.rgb += prevBounce.rgb * 0.2 * SEGISecondaryBounceGain * tex.rgb * color.rgb;

					 
					float4 result = float4(col.rgb, 2.0);

					
					const float sqrt2 = sqrt(2.0) * 1.2;

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
						interlockedAddFloat4b(RG0, coord - int3((int)(input.normal.x * sqrt2 * 1.0), (int)(input.normal.y * sqrt2 * 1.0), (int)(input.normal.z * sqrt2 * 1.0)), float4(0.0, 0.0, 0.0, 14.0));
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
