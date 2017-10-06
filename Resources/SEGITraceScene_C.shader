Shader "Hidden/SEGITraceScene_C" {
	Properties
	{
		_Color ("Main Color", Color) = (1,1,1,1)
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_EmissionColor("Color", Color) = (0,0,0)
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.333
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

				#define PI 3.14159265
				
				RWTexture3D<uint> RG0;
				
				sampler3D SEGIVolumeLevel0;
				sampler3D SEGIVolumeLevel1;
				sampler3D SEGIVolumeLevel2;
				sampler3D SEGIVolumeLevel3;
				sampler3D SEGIVolumeLevel4;
				sampler3D SEGIVolumeLevel5;
				
				float4x4 SEGIVoxelViewFront;
				float4x4 SEGIVoxelViewLeft;
				float4x4 SEGIVoxelViewTop;

				half4 _EmissionColor;
				float _Cutoff;

				half4 _Color;
				float SEGISecondaryOcclusionStrength;

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
				

				float4x4 SEGIVoxelToGIProjection;
				float4x4 SEGIVoxelProjectionInverse;
				sampler2D SEGIGIDepthNormalsTexture;
				float4 SEGISunlightVector;
				float4 GISunColor;
				int SEGIFrameSwitch;
				half4 SEGISkyColor;
				float SEGISoftSunlight;
				int SEGISecondaryCones;
				
				sampler3D SEGIVolumeTexture0;
				float SEGIVoxelScaleFactor;
				int SEGIVoxelAA;
				int SEGISphericalSkylight;


				float4 SEGICurrentClipTransform;
				float4 SEGIClipTransform0;
				float4 SEGIClipTransform1;
				float4 SEGIClipTransform2;
				float4 SEGIClipTransform3;
				float4 SEGIClipTransform4;
				float4 SEGIClipTransform5;

				float4 SEGIClipmapOverlap;


				#define VoxelResolution (SEGIVoxelResolution)

				float3 TransformClipSpaceInverse(float3 pos, float4 transform)
				{
					pos += transform.xyz;
					pos = pos * 2.0 - 1.0;
					pos /= transform.w;
					pos = pos * 0.5 + 0.5;

					return pos;
				}

				float3 TransformClipSpace(float3 pos, float4 transform)
				{
					pos = pos * 2.0 - 1.0;
					pos *= transform.w;
					pos = pos * 0.5 + 0.5;
					pos -= transform.xyz;

					return pos;
				}

				float3 TransformClipSpace1(float3 pos)
				{
					return TransformClipSpace(pos, SEGIClipTransform1);
				}

				float3 TransformClipSpace2(float3 pos)
				{
					return TransformClipSpace(pos, SEGIClipTransform2);
				}

				float3 TransformClipSpace3(float3 pos)
				{
					return TransformClipSpace(pos, SEGIClipTransform3);
				}

				float3 TransformClipSpace4(float3 pos)
				{
					return TransformClipSpace(pos, SEGIClipTransform4);
				}

				float3 TransformClipSpace5(float3 pos)
				{
					return TransformClipSpace(pos, SEGIClipTransform5);
				}

				float GISampleWeight(float3 pos)
				{
					float weight = 1.0;

					if (pos.x < 0.0 || pos.x > 1.0 ||
						pos.y < 0.0 || pos.y > 1.0 ||
						pos.z < 0.0 || pos.z > 1.0)
					{
						weight = 0.0;
					}

					return weight;
				}

					
				float4 ConeTrace(float3 voxelOrigin, float3 kernel, float3 worldNormal)
				{


					float skyVisibility = 1.0;		
					
					float3 gi = float3(0,0,0);	
					
					const int numSteps = 7;	
					
					float3 adjustedKernel = normalize(kernel + worldNormal * 0.2);	
					


					float dist = length(voxelOrigin * 2.0 - 1.0);
					
					int startMipLevel = 0;

					voxelOrigin = TransformClipSpaceInverse(voxelOrigin, SEGICurrentClipTransform);
					voxelOrigin.xyz += worldNormal.xyz * 0.016;


					const float width = 3.38;
					const float farOcclusionStrength = 4.0;
					const float occlusionPower = 1.05;

				
					for (int i = 0; i < numSteps; i++)
					{
						float fi = ((float)i) / numSteps;		
						fi = lerp(fi, 1.0, 0.001);
						
						float coneDistance = (exp2(fi * 4.0) - 0.99) / 8.0; 
										
						float coneSize = coneDistance * width * 10.3;

						float3 voxelCheckCoord = voxelOrigin.xyz + adjustedKernel.xyz * (coneDistance * 1.12 * 1.0);

						float4 sample = float4(0.0, 0.0, 0.0, 0.0);
						int mipLevel = floor(coneSize);

						mipLevel = max(startMipLevel, log2(pow(fi, 1.3) * 24.0 * width + 1.0));

						

						if (mipLevel == 0 || mipLevel == 1)
						{
							voxelCheckCoord = TransformClipSpace1(voxelCheckCoord);
							sample = tex3Dlod(SEGIVolumeLevel1, float4(voxelCheckCoord.xyz, coneSize)) * GISampleWeight(voxelCheckCoord);
						}
						else if (mipLevel == 2)
						{
							voxelCheckCoord = TransformClipSpace2(voxelCheckCoord);
							sample = tex3Dlod(SEGIVolumeLevel2, float4(voxelCheckCoord.xyz, coneSize)) * GISampleWeight(voxelCheckCoord);
						}
						else if (mipLevel == 3)
						{
							voxelCheckCoord = TransformClipSpace3(voxelCheckCoord);
							sample = tex3Dlod(SEGIVolumeLevel3, float4(voxelCheckCoord.xyz, coneSize)) * GISampleWeight(voxelCheckCoord);
						}
						else if (mipLevel == 4)
						{
							voxelCheckCoord = TransformClipSpace4(voxelCheckCoord);
							sample = tex3Dlod(SEGIVolumeLevel4, float4(voxelCheckCoord.xyz, coneSize)) * GISampleWeight(voxelCheckCoord);
						}
						else
						{
							voxelCheckCoord = TransformClipSpace5(voxelCheckCoord);
							sample = tex3Dlod(SEGIVolumeLevel5, float4(voxelCheckCoord.xyz, coneSize)) * GISampleWeight(voxelCheckCoord);
						}
						
						float occlusion = skyVisibility;
						
						float falloffFix = pow(fi, 2.0) * 4.0 + 0.0;

						gi.rgb += sample.rgb * (coneSize * 1.0 + 1.0) * occlusion * falloffFix;

						skyVisibility *= pow(saturate(1.0 - sample.a * SEGISecondaryOcclusionStrength * (1.0 + coneDistance * farOcclusionStrength)), 1.0 * occlusionPower);

						
					}


					float NdotL = pow(saturate(dot(worldNormal, kernel) * 1.0 - 0.0), 1.0);

					gi *= NdotL;
					skyVisibility *= NdotL;

					skyVisibility *= lerp(saturate(dot(kernel, float3(0.0, 1.0, 0.0)) * 10.0 + 0.0), 1.0, SEGISphericalSkylight);

					float3 skyColor = float3(0.0, 0.0, 0.0);

					float upGradient = saturate(dot(kernel, float3(0.0, 1.0, 0.0)));
					float sunGradient = saturate(dot(kernel, -SEGISunlightVector.xyz));
					skyColor += lerp(SEGISkyColor.rgb * 1.0, SEGISkyColor.rgb * 0.5, pow(upGradient, (0.5).xxx));
					skyColor += GISunColor.rgb * pow(sunGradient, (4.0).xxx) * SEGISoftSunlight;


					gi += skyColor * skyVisibility * 10.0;

					return float4(gi.rgb, 0.0f);
				}
				
				float2 rand(float3 coord)
				{
					float noiseX = saturate(frac(sin(dot(coord, float3(12.9898, 78.223, 35.3820))) * 43758.5453));
					float noiseY = saturate(frac(sin(dot(coord, float3(12.9898, 78.223, 35.2879)*2.0)) * 43758.5453));
					
					return float2(noiseX, noiseY);
				}

				float4 frag (g2f input) : SV_TARGET
				{
					int3 coord = int3((int)(input.pos.x), (int)(input.pos.y), (int)(input.pos.z * VoxelResolution));
					
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
					
					float3 fcoord = (float3)coord.xyz / VoxelResolution;

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


					
					float3 gi = (0.0).xxx;
					
					float3 worldNormal = input.normal;
					
					float3 voxelOrigin = (fcoord + worldNormal.xyz * 0.006 * 1.0);
					
					float4 traceResult = float4(0,0,0,0);
					
					float2 dither = rand(fcoord);
					
					const float phi = 1.618033988;
					const float gAngle = phi * PI * 2.0;
					
					
					const int numSamples = SEGISecondaryCones;
					for (int i = 0; i < numSamples; i++)
					{
						float fi = (float)i; 
						float fiN = fi / numSamples;
						float longitude = gAngle * fi;
						float latitude = asin(fiN * 2.0 - 1.0);
						
						float3 kernel;
						kernel.x = cos(latitude) * cos(longitude);
						kernel.z = cos(latitude) * sin(longitude);
						kernel.y = sin(latitude);
						
						kernel = normalize(kernel + worldNormal.xyz);

						if (i == 0)
						{
							kernel = float3(0.0, 1.0, 0.0);
						}

							traceResult += ConeTrace(voxelOrigin.xyz, kernel.xyz, worldNormal.xyz);
					}
					
					traceResult /= numSamples;
					
					
					gi.rgb = traceResult.rgb;
					
					gi.rgb *= 4.3;
					
					gi.rgb += traceResult.a * 1.0 * SEGISkyColor;

					
					float4 result = float4(gi.rgb, 2.0);


					interlockedAddFloat4(RG0, coord, result);
					
					return float4(0.0, 0.0, 0.0, 0.0);
				}
			
			ENDCG
		}
	} 
	FallBack Off
}
