float SEGIVoxelScaleFactor;

int StochasticSampling;
int TraceDirections;
int TraceSteps;
float TraceLength;
float ConeSize;
float OcclusionStrength;
float OcclusionPower;
float ConeTraceBias;
float GIGain;
float NearLightGain;
float NearOcclusionStrength;
float SEGISoftSunlight;
float FarOcclusionStrength;
float FarthestOcclusionStrength;


half4 GISunColor;

sampler3D SEGIVolumeLevel0;
sampler3D SEGIVolumeLevel1;
sampler3D SEGIVolumeLevel2;
sampler3D SEGIVolumeLevel3;
sampler3D SEGIVolumeLevel4;
sampler3D SEGIVolumeLevel5;
sampler3D SEGIVolumeLevel6;
sampler3D SEGIVolumeLevel7;
sampler3D VolumeTexture1;
sampler3D VolumeTexture2;
sampler3D VolumeTexture3;

float4x4 SEGIVoxelProjection;
float4x4 SEGIWorldToVoxel;
float4x4 GIProjectionInverse;
float4x4 GIToWorld;

float4x4 GIToVoxelProjection;


half4 SEGISkyColor;

float4 SEGISunlightVector;

int ReflectionSteps;


uniform half4 _MainTex_TexelSize;

float4x4 ProjectionMatrixInverse;

sampler2D _CameraDepthNormalsTexture;
sampler2D _CameraDepthTexture;
sampler2D _MainTex;
sampler2D PreviousGITexture;
sampler2D _CameraGBufferTexture0;
sampler2D _CameraMotionVectorsTexture;
float4x4 WorldToCamera;
float4x4 ProjectionMatrix;

int SEGISphericalSkylight;

float GetDepthTexture(float2 coord)
{
#if defined(UNITY_REVERSED_Z)
	return 1.0 - tex2Dlod(_CameraDepthTexture, float4(coord.x, coord.y, 0.0, 0.0)).x;
#else
	return tex2Dlod(_CameraDepthTexture, float4(coord.x, coord.y, 0.0, 0.0)).x;
#endif
}

float4 GetViewSpacePosition(float2 coord)
{
	float depth = GetDepthTexture(coord);

	float4 viewPosition = mul(ProjectionMatrixInverse, float4(coord.x * 2.0 - 1.0, coord.y * 2.0 - 1.0, 2.0 * depth - 1.0, 1.0));
	viewPosition /= viewPosition.w;

	return viewPosition;
}

float3 ProjectBack(float4 viewPos)
{
	viewPos = mul(ProjectionMatrix, float4(viewPos.xyz, 0.0));
	viewPos.xyz /= viewPos.w;
	viewPos.xyz = viewPos.xyz * 0.5 + 0.5;
	return viewPos.xyz;
}


float2 rand(float2 coord)
{
	float noiseX = saturate(frac(sin(dot(coord, float2(12.9898, 78.223))) * 43758.5453));
	float noiseY = saturate(frac(sin(dot(coord, float2(12.9898, 78.223)*2.0)) * 43758.5453));

	return float2(noiseX, noiseY);
}

float4 ConeTrace(float3 voxelOrigin, float3 kernel, float3 worldNormal, float2 uv, float noise, int steps, float width, float lengthMult, float skyMult)
{
	float skyVisibility = 1.0;

	float3 gi = float3(0, 0, 0);

	int numSteps = (int)(steps * lerp(SEGIVoxelScaleFactor, 1.0, 0.5));

	float3 adjustedKernel = normalize(kernel.xyz + worldNormal.xyz * 0.00 * width);


	for (int i = 0; i < numSteps; i++)
	{
		float fi = ((float)i) / numSteps;
		fi = lerp(fi, 1.0, 0.01);

		float coneDistance = (exp2(fi * 4.0) - 0.9) / 8.0;

		coneDistance -= 0.00;

		float coneSize = fi * width * lerp(SEGIVoxelScaleFactor, 1.0, 0.5); 
																			
		float3 voxelCheckCoord = voxelOrigin.xyz + adjustedKernel.xyz * (coneDistance * 0.12 * TraceLength * lengthMult + 0.001);

		float4 sample = float4(0.0, 0.0, 0.0, 0.0);
		int mipLevel = floor(coneSize);
		if (mipLevel == 0)
			sample = tex3Dlod(SEGIVolumeLevel0, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 1)
			sample = tex3Dlod(SEGIVolumeLevel1, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 2)
			sample = tex3Dlod(SEGIVolumeLevel2, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 3)
			sample = tex3Dlod(SEGIVolumeLevel3, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 4)
			sample = tex3Dlod(SEGIVolumeLevel4, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 5)
			sample = tex3Dlod(SEGIVolumeLevel5, float4(voxelCheckCoord.xyz, coneSize));
		else
			sample = float4(1, 1, 1, 0);


		float occlusion = skyVisibility * skyVisibility;

		float falloffFix = pow(fi, 1.0) * 4.0 + NearLightGain;

		sample.a *= lerp(saturate(coneSize / 1.0), 1.0, NearOcclusionStrength);
		gi.rgb += sample.rgb * (coneSize * 1.0 + 1.0) * occlusion * falloffFix;

		skyVisibility *= pow(saturate(1.0 - (sample.a) * (coneSize * 0.2 * FarOcclusionStrength + 1.0 + coneSize * coneSize * 0.05 * FarthestOcclusionStrength) * OcclusionStrength), lerp(0.014, 1.5 * OcclusionPower, min(1.0, coneSize / 5.0)));
	}

	float NdotL = pow(saturate(dot(worldNormal, kernel) * 1.0 - 0.0), 0.5);

	gi *= NdotL;
	skyVisibility *= NdotL;
	if (StochasticSampling > 0)
	{
		skyVisibility *= lerp(saturate(dot(kernel, float3(0.0, 1.0, 0.0)) * 10.0 + 0.0), 1.0, SEGISphericalSkylight);
	}
	else
	{
		skyVisibility *= lerp(saturate(dot(kernel, float3(0.0, 1.0, 0.0)) * 10.0 + 0.0), 1.0, SEGISphericalSkylight);
	}

	float3 skyColor = float3(0.0, 0.0, 0.0);

	float upGradient = saturate(dot(kernel, float3(0.0, 1.0, 0.0)));
	float sunGradient = saturate(dot(kernel, -SEGISunlightVector.xyz));
	skyColor += lerp(SEGISkyColor.rgb * 1.0, SEGISkyColor.rgb * 0.5, pow(upGradient, (0.5).xxx));
	skyColor += GISunColor.rgb * pow(sunGradient, (4.0).xxx) * SEGISoftSunlight;

	gi.rgb *= GIGain * 0.25;

	gi += skyColor * skyVisibility * skyMult;

	return float4(gi.rgb * 0.8, 0.0f);
}




float ReflectionOcclusionPower;
float SkyReflectionIntensity;

float4 SpecularConeTrace(float3 voxelOrigin, float3 kernel, float3 worldNormal, float smoothness, float2 uv, float dither)
{
	float skyVisibility = 1.0;

	float3 gi = float3(0, 0, 0);

	float coneLength = 6.0;
	float coneSizeScalar = lerp(1.3, 0.05, smoothness) * coneLength;

	float3 adjustedKernel = normalize(kernel.xyz + worldNormal.xyz * 0.2 * (1.0 - smoothness));

	int numSamples = (int)(lerp(uint(ReflectionSteps) / uint(5), ReflectionSteps, smoothness));

	for (int i = 0; i < numSamples; i++)
	{
		float fi = ((float)i) / numSamples;

		float coneSize = fi * coneSizeScalar;

		float coneDistance = (exp2(fi * coneSizeScalar) - 0.998) / exp2(coneSizeScalar);

		float3 voxelCheckCoord = voxelOrigin.xyz + adjustedKernel.xyz * (coneDistance * 0.12 * coneLength + 0.001);

		float4 sample = float4(0.0, 0.0, 0.0, 0.0);
		coneSize = pow(coneSize / 5.0, 2.0) * 5.0;
		int mipLevel = floor(coneSize);
		if (mipLevel == 0)
		{
			sample = tex3Dlod(SEGIVolumeLevel0, float4(voxelCheckCoord.xyz, coneSize));
			sample = lerp(sample, tex3Dlod(SEGIVolumeLevel1, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
		}
		else if (mipLevel == 1)
		{
			sample = tex3Dlod(SEGIVolumeLevel1, float4(voxelCheckCoord.xyz, coneSize));
			sample = lerp(sample, tex3Dlod(SEGIVolumeLevel2, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
		}
		else if (mipLevel == 2)
		{
			sample = tex3Dlod(SEGIVolumeLevel2, float4(voxelCheckCoord.xyz, coneSize));
			sample = lerp(sample, tex3Dlod(SEGIVolumeLevel3, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
		}
		else if (mipLevel == 3)
		{
			sample = tex3Dlod(SEGIVolumeLevel3, float4(voxelCheckCoord.xyz, coneSize));
			sample = lerp(sample, tex3Dlod(SEGIVolumeLevel4, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
		}
		else if (mipLevel == 4)
		{
			sample = tex3Dlod(SEGIVolumeLevel4, float4(voxelCheckCoord.xyz, coneSize));
			sample = lerp(sample, tex3Dlod(SEGIVolumeLevel5, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
		}
		else if (mipLevel == 5)
			sample = tex3Dlod(SEGIVolumeLevel5, float4(voxelCheckCoord.xyz, coneSize));
		else
			sample = float4(0, 0, 0, 0);

		float occlusion = skyVisibility;

		float falloffFix = fi * 6.0 + 0.6;

		gi.rgb += sample.rgb * (coneSize * 5.0 + 1.0) * occlusion * 0.5;
		sample.a *= lerp(saturate(fi / 0.2), 1.0, NearOcclusionStrength);
		skyVisibility *= pow(saturate(1.0 - sample.a * 0.5), (lerp(4.0, 1.0, smoothness) + coneSize * 0.5) * ReflectionOcclusionPower);
	}

	skyVisibility *= saturate(dot(worldNormal, kernel) * 0.7 + 0.3);
	skyVisibility *= lerp(saturate(dot(kernel, float3(0.0, 1.0, 0.0)) * 10.0), 1.0, SEGISphericalSkylight);

	gi *= saturate(dot(worldNormal, kernel) * 10.0);

	return float4(gi.rgb * 4.0, skyVisibility);
}

float4 VisualConeTrace(float3 voxelOrigin, float3 kernel)
{
	float skyVisibility = 1.0;

	float3 gi = float3(0, 0, 0);

	float coneLength = 6.0;
	float coneSizeScalar = 0.25 * coneLength;

	for (int i = 0; i < 423; i++)
	{
		float fi = ((float)i) / 423;

		float coneSize = fi * coneSizeScalar;

		float3 voxelCheckCoord = voxelOrigin.xyz + kernel.xyz * (0.12 * coneLength * fi * fi + 0.005);

		float4 sample = float4(0.0, 0.0, 0.0, 0.0);

		sample = tex3Dlod(SEGIVolumeLevel0, float4(voxelCheckCoord.xyz, coneSize));

		float occlusion = skyVisibility;

		float falloffFix = fi * 6.0 + 0.6;

		gi.rgb += sample.rgb * (coneSize * 5.0 + 1.0) * occlusion * 0.5;
		skyVisibility *= saturate(1.0 - sample.a);
	}

	return float4(gi.rgb, skyVisibility);
}