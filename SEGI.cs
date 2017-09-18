#pragma warning disable 0618
#pragma warning disable 0414

using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Sonic Ether/SEGI")]
public class SEGI : MonoBehaviour
{
	object initChecker;

	Material material;
	Camera attachedCamera;
	Transform shadowCamTransform;

	Camera shadowCam;
	GameObject shadowCamGameObject;

	[Serializable]
	public enum VoxelResolution
	{
		low = 128,
		high = 256
	}

	public VoxelResolution voxelResolution = VoxelResolution.high;

	public bool visualizeSunDepthTexture = false;
	public bool visualizeGI = false;

	public Light sun;
	public LayerMask giCullingMask = 2147483647;

	public float shadowSpaceSize = 50.0f;

	[Range(0.01f, 1.0f)]
	public float temporalBlendWeight = 0.1f;

	public bool visualizeVoxels = false;

	public bool updateGI = true;


	public Color skyColor;

	public float voxelSpaceSize = 50.0f;

	public bool useBilateralFiltering = false;

	[Range(0, 2)]
	public int innerOcclusionLayers = 1;


    Texture2D[] blueNoise;


	public bool halfResolution = true;
	public bool stochasticSampling = true;
	public bool infiniteBounces = false;
	public Transform followTransform;
	[Range(1, 128)]
	public int cones = 6;
	[Range(1, 32)]
	public int coneTraceSteps = 14;
	[Range(0.1f, 2.0f)]
	public float coneLength = 1.0f;
	[Range(0.5f, 6.0f)]
	public float coneWidth = 5.5f;
	[Range(0.0f, 4.0f)]
	public float occlusionStrength = 1.0f;
	[Range(0.0f, 4.0f)]
	public float nearOcclusionStrength = 0.5f;
	[Range(0.001f, 4.0f)]
	public float occlusionPower = 1.5f;
	[Range(0.0f, 4.0f)]
	public float coneTraceBias = 1.0f;
	[Range(0.0f, 4.0f)]
	public float nearLightGain = 1.0f;
	[Range(0.0f, 4.0f)]
	public float giGain = 1.0f;
	[Range(0.0f, 4.0f)]
	public float secondaryBounceGain = 1.0f;
	[Range(0.0f, 16.0f)]
	public float softSunlight = 0.0f;

	[Range(0.0f, 8.0f)]
	public float skyIntensity = 1.0f;

	public bool doReflections = true;
	[Range(12, 128)]
	public int reflectionSteps = 64;
	[Range(0.001f, 4.0f)]
	public float reflectionOcclusionPower = 1.0f;
	[Range(0.0f, 1.0f)]
	public float skyReflectionIntensity = 1.0f;



	[Range(0.1f, 4.0f)]
	public float farOcclusionStrength = 1.0f;
	[Range(0.1f, 4.0f)]
	public float farthestOcclusionStrength = 1.0f;

	[Range(3, 16)]
	public int secondaryCones = 6;
	[Range(0.1f, 4.0f)]
	public float secondaryOcclusionStrength = 1.0f;

	public bool sphericalSkylight = false;

	struct Pass
	{
		public static int DiffuseTrace = 0;
		public static int BilateralBlur = 1;
		public static int BlendWithScene = 2;
#if UNITY_5_4_OR_NEWER
		public static int TemporalBlend = 3;
#else
		public static int TemporalBlend = 12;
#endif
		public static int SpecularTrace = 4;
		public static int GetCameraDepthTexture = 5;
		public static int GetWorldNormals = 6;
		public static int VisualizeGI = 7;
		public static int WriteBlack = 8;
		public static int VisualizeVoxels = 10;
		public static int BilateralUpsample = 11;
	}

	public struct SystemSupported
	{
		public bool hdrTextures;
		public bool rIntTextures;
		public bool dx11;
		public bool volumeTextures;
		public bool postShader;
		public bool sunDepthShader;
		public bool voxelizationShader;
		public bool tracingShader;

		public bool fullFunctionality
		{
			get
			{
				return hdrTextures && rIntTextures && dx11 && volumeTextures && postShader && sunDepthShader && voxelizationShader && tracingShader;
			}
		}
	}

	/// <summary>
	/// Contains info on system compatibility of required hardware functionality
	/// </summary>
	public SystemSupported systemSupported;

	/// <summary>
	/// Estimates the VRAM usage of all the render textures used to render GI.
	/// </summary>
	public float vramUsage
	{
		get
		{
			long v = 0;

			if (sunDepthTexture != null)
				v += sunDepthTexture.width * sunDepthTexture.height * 16;

			if (previousResult != null)
				v += previousResult.width * previousResult.height * 16 * 4;

			if (previousDepth != null)
				v += previousDepth.width * previousDepth.height * 32;

			if (intTex1 != null)
				v += intTex1.width * intTex1.height * intTex1.volumeDepth * 32;

			if (volumeTextures != null)
			{
				for (int i = 0; i < volumeTextures.Length; i++)
				{
					if (volumeTextures[i] != null)
						v += volumeTextures[i].width * volumeTextures[i].height * volumeTextures[i].volumeDepth * 16 * 4;
				}
			}

			if (volumeTexture1 != null)
				v += volumeTexture1.width * volumeTexture1.height * volumeTexture1.volumeDepth * 16 * 4;

			if (volumeTextureB != null)
				v += volumeTextureB.width * volumeTextureB.height * volumeTextureB.volumeDepth * 16 * 4;

			if (dummyVoxelTexture != null)
				v += dummyVoxelTexture.width * dummyVoxelTexture.height * 8;

			if (dummyVoxelTexture2 != null)
				v += dummyVoxelTexture2.width * dummyVoxelTexture2.height * 8;

			float vram = (v / 8388608.0f);

			return vram;
		}
	}





	public bool gaussianMipFilter = false;

	int mipFilterKernel
	{
		get
		{
			return gaussianMipFilter ? 1 : 0;
		}
	}

	public bool voxelAA = false;

	int dummyVoxelResolution
	{
		get
		{
			return (int)voxelResolution * (voxelAA ? 2 : 1);
		}
	}

	int sunShadowResolution = 256;
	int prevSunShadowResolution;





	Shader sunDepthShader;

	float shadowSpaceDepthRatio = 10.0f;

	int frameSwitch = 0;

	RenderTexture sunDepthTexture;
	RenderTexture previousResult;
	RenderTexture previousDepth;
	RenderTexture intTex1;
	RenderTexture[] volumeTextures;
	RenderTexture volumeTexture1;
	RenderTexture volumeTextureB;

	RenderTexture activeVolume;
	RenderTexture previousActiveVolume;

	RenderTexture dummyVoxelTexture;
	RenderTexture dummyVoxelTexture2;

	bool dontTry = false;

	Shader voxelizationShader;
	Shader voxelTracingShader;

	ComputeShader clearCompute;
	ComputeShader transferInts;
	ComputeShader mipFilter;

	const int numMipLevels = 6;

	Camera voxelCamera;
	GameObject voxelCameraGO;
	GameObject leftViewPoint;
	GameObject topViewPoint;

	float voxelScaleFactor
	{
		get
		{
			return (float)voxelResolution / 256.0f;
		}
	}

	Vector3 voxelSpaceOrigin;
	Vector3 previousVoxelSpaceOrigin;
	Vector3 voxelSpaceOriginDelta;


	Quaternion rotationFront = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
	Quaternion rotationLeft = new Quaternion(0.0f, 0.7f, 0.0f, 0.7f);
	Quaternion rotationTop = new Quaternion(0.7f, 0.0f, 0.0f, 0.7f);

	int voxelFlipFlop = 0;


	int giRenderRes
	{
		get
		{
			return halfResolution ? 2 : 1;
		}
	}

	enum RenderState
	{
		Voxelize,
		Bounce
	}

	RenderState renderState = RenderState.Voxelize;



	public void LoadAndApplyPreset(string path)
	{
		SEGIPreset preset = Resources.Load<SEGIPreset>(path);

		ApplyPreset(preset);
	}

	public void ApplyPreset(SEGIPreset preset)
	{
		voxelResolution = preset.voxelResolution;
		voxelAA = preset.voxelAA;
		innerOcclusionLayers = preset.innerOcclusionLayers;
		infiniteBounces = preset.infiniteBounces;

		temporalBlendWeight = preset.temporalBlendWeight;
		useBilateralFiltering = preset.useBilateralFiltering;
		halfResolution = preset.halfResolution;
		stochasticSampling = preset.stochasticSampling;
		doReflections = preset.doReflections;

		cones = preset.cones;
		coneTraceSteps = preset.coneTraceSteps;
		coneLength = preset.coneLength;
		coneWidth = preset.coneWidth;
		coneTraceBias = preset.coneTraceBias;
		occlusionStrength = preset.occlusionStrength;
		nearOcclusionStrength = preset.nearOcclusionStrength;
		occlusionPower = preset.occlusionPower;
		nearLightGain = preset.nearLightGain;
		giGain = preset.giGain;
		secondaryBounceGain = preset.secondaryBounceGain;

		reflectionSteps = preset.reflectionSteps;
		reflectionOcclusionPower = preset.reflectionOcclusionPower;
		skyReflectionIntensity = preset.skyReflectionIntensity;
		gaussianMipFilter = preset.gaussianMipFilter;

		farOcclusionStrength = preset.farOcclusionStrength;
		farthestOcclusionStrength = preset.farthestOcclusionStrength;
		secondaryCones = preset.secondaryCones;
		secondaryOcclusionStrength = preset.secondaryOcclusionStrength;
	}

	void Start()
	{
		InitCheck();
	}

	void InitCheck()
	{
		if (initChecker == null)
		{
			Init();
		}
	}

	void CreateVolumeTextures()
	{
		volumeTextures = new RenderTexture[numMipLevels];

		for (int i = 0; i < numMipLevels; i++)
		{
			if (volumeTextures[i])
			{
				volumeTextures[i].DiscardContents();
				volumeTextures[i].Release();
				DestroyImmediate(volumeTextures[i]);
			}
			int resolution = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i));
			volumeTextures[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
			volumeTextures[i].isVolume = true;
			volumeTextures[i].volumeDepth = resolution;
			volumeTextures[i].enableRandomWrite = true;
			volumeTextures[i].filterMode = FilterMode.Bilinear;
			#if UNITY_5_4_OR_NEWER
			volumeTextures[i].autoGenerateMips = false;
			#else
			volumeTextures[i].generateMips = false;
			#endif
			volumeTextures[i].useMipMap = false;
			volumeTextures[i].Create();
			volumeTextures[i].hideFlags = HideFlags.HideAndDontSave;
		}

		if (volumeTextureB)
		{
			volumeTextureB.DiscardContents();
			volumeTextureB.Release();
			DestroyImmediate(volumeTextureB);
		}
		volumeTextureB = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		volumeTextureB.isVolume = true;
		volumeTextureB.volumeDepth = (int)voxelResolution;
		volumeTextureB.enableRandomWrite = true;
		volumeTextureB.filterMode = FilterMode.Bilinear;
		#if UNITY_5_4_OR_NEWER
		volumeTextureB.autoGenerateMips = false;
		#else
		volumeTextureB.generateMips = false;
		#endif
		volumeTextureB.useMipMap = false;
		volumeTextureB.Create();
		volumeTextureB.hideFlags = HideFlags.HideAndDontSave;

		if (volumeTexture1)
		{
			volumeTexture1.DiscardContents();
			volumeTexture1.Release();
			DestroyImmediate(volumeTexture1);
		}
		volumeTexture1 = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		volumeTexture1.isVolume = true;
		volumeTexture1.volumeDepth = (int)voxelResolution;
		volumeTexture1.enableRandomWrite = true;
		volumeTexture1.filterMode = FilterMode.Point;
		#if UNITY_5_4_OR_NEWER
		volumeTexture1.autoGenerateMips = false;
		#else
		volumeTexture1.generateMips = false;
		#endif
		volumeTexture1.useMipMap = false;
		volumeTexture1.antiAliasing = 1;
		volumeTexture1.Create();
		volumeTexture1.hideFlags = HideFlags.HideAndDontSave;



		if (intTex1)
		{
			intTex1.DiscardContents();
			intTex1.Release();
			DestroyImmediate(intTex1);
		}
		intTex1 = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.RInt, RenderTextureReadWrite.Linear);
		intTex1.isVolume = true;
		intTex1.volumeDepth = (int)voxelResolution;
		intTex1.enableRandomWrite = true;
		intTex1.filterMode = FilterMode.Point;
		intTex1.Create();
		intTex1.hideFlags = HideFlags.HideAndDontSave;

		ResizeDummyTexture();

	}

	void ResizeDummyTexture()
	{
		if (dummyVoxelTexture)
		{
			dummyVoxelTexture.DiscardContents();
			dummyVoxelTexture.Release();
			DestroyImmediate(dummyVoxelTexture);
		}
		dummyVoxelTexture = new RenderTexture(dummyVoxelResolution, dummyVoxelResolution, 0, RenderTextureFormat.R8);
		dummyVoxelTexture.Create();
		dummyVoxelTexture.hideFlags = HideFlags.HideAndDontSave;

		if (dummyVoxelTexture2)
		{
			dummyVoxelTexture2.DiscardContents();
			dummyVoxelTexture2.Release();
			DestroyImmediate(dummyVoxelTexture2);
		}
		dummyVoxelTexture2 = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.R8);
		dummyVoxelTexture2.Create();
		dummyVoxelTexture2.hideFlags = HideFlags.HideAndDontSave;
	}

	void Init()
	{
		sunDepthShader = Shader.Find("Hidden/SEGIRenderSunDepth");

		material = new Material(Shader.Find("Hidden/SEGI"));
		material.hideFlags = HideFlags.HideAndDontSave;
		attachedCamera = this.GetComponent<Camera>();
#if UNITY_5_4_OR_NEWER
		attachedCamera.depthTextureMode |= DepthTextureMode.Depth;
		attachedCamera.depthTextureMode |= DepthTextureMode.MotionVectors;
#else
		attachedCamera.depthTextureMode |= DepthTextureMode.Depth;
#endif

		GameObject scgo = GameObject.Find("SEGI_SHADOWCAM");

		clearCompute = Resources.Load("SEGIClear") as ComputeShader;
		transferInts = Resources.Load("SEGITransferInts") as ComputeShader;
		mipFilter = Resources.Load("SEGIMipFilter") as ComputeShader;

		if (!scgo)
		{
			shadowCamGameObject = new GameObject("SEGI_SHADOWCAM");
			shadowCam = shadowCamGameObject.AddComponent<Camera>();
			shadowCamGameObject.hideFlags = HideFlags.HideAndDontSave;


			shadowCam.enabled = false;
			shadowCam.depth = attachedCamera.depth - 1;
			shadowCam.orthographic = true;
			shadowCam.orthographicSize = shadowSpaceSize;
			shadowCam.clearFlags = CameraClearFlags.SolidColor;
			shadowCam.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
			shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;
			shadowCam.cullingMask = giCullingMask;
			shadowCam.useOcclusionCulling = false;

			shadowCamTransform = shadowCamGameObject.transform;
		}
		else
		{
			shadowCamGameObject = scgo;
			shadowCam = scgo.GetComponent<Camera>();
			shadowCamTransform = shadowCamGameObject.transform;
		}

		if (sunDepthTexture)
		{
			sunDepthTexture.DiscardContents();
			sunDepthTexture.Release();
			DestroyImmediate(sunDepthTexture);
		}
		sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
		sunDepthTexture.wrapMode = TextureWrapMode.Clamp;
		sunDepthTexture.filterMode = FilterMode.Point;
		sunDepthTexture.Create();
		sunDepthTexture.hideFlags = HideFlags.HideAndDontSave;





		voxelizationShader = Shader.Find("Hidden/SEGIVoxelizeScene");
		voxelTracingShader = Shader.Find("Hidden/SEGITraceScene");

		CreateVolumeTextures();



		GameObject vcgo = GameObject.Find("SEGI_VOXEL_CAMERA");
		if (vcgo)
			DestroyImmediate(vcgo);

		voxelCameraGO = new GameObject("SEGI_VOXEL_CAMERA");
		voxelCameraGO.hideFlags = HideFlags.HideAndDontSave;

		voxelCamera = voxelCameraGO.AddComponent<Camera>();
		voxelCamera.enabled = false;
		voxelCamera.orthographic = true;
		voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
		voxelCamera.nearClipPlane = 0.0f;
		voxelCamera.farClipPlane = voxelSpaceSize;
		voxelCamera.depth = -2;
		voxelCamera.renderingPath = RenderingPath.Forward;
		voxelCamera.clearFlags = CameraClearFlags.Color;
		voxelCamera.backgroundColor = Color.black;
		voxelCamera.useOcclusionCulling = false;

		GameObject lvp = GameObject.Find("SEGI_LEFT_VOXEL_VIEW");
		if (lvp)
			DestroyImmediate(lvp);

		leftViewPoint = new GameObject("SEGI_LEFT_VOXEL_VIEW");
		leftViewPoint.hideFlags = HideFlags.HideAndDontSave;

		GameObject tvp = GameObject.Find("SEGI_TOP_VOXEL_VIEW");
		if (tvp)
			DestroyImmediate(tvp);

		topViewPoint = new GameObject("SEGI_TOP_VOXEL_VIEW");
		topViewPoint.hideFlags = HideFlags.HideAndDontSave;



        //Get blue noise textures
        blueNoise = null;
        blueNoise = new Texture2D[64];
        for (int i = 0; i < 64; i++)
        {
            string fileName = "LDR_RGBA_" + i.ToString();
            //Texture2D blueNoiseTexture = AssetDatabase.LoadAssetAtPath("Assets/SEGI/Resources/Noise Textures/" + fileName, typeof(Texture2D)) as Texture2D;
            Texture2D blueNoiseTexture = Resources.Load("Noise Textures/" + fileName) as Texture2D;

            if (blueNoiseTexture == null)
            {
                Debug.LogWarning("Unable to find noise texture \"Assets/SEGI/Resources/Noise Textures/" + fileName + "\" for SEGI!");
            } 

            blueNoise[i] = blueNoiseTexture;

        }
        




		initChecker = new object();
	}

	void CheckSupport()
	{
		systemSupported.hdrTextures = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
		systemSupported.rIntTextures = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RInt);
		systemSupported.dx11 = SystemInfo.graphicsShaderLevel >= 50 && SystemInfo.supportsComputeShaders;
		systemSupported.volumeTextures = SystemInfo.supports3DTextures;

		systemSupported.postShader = material.shader.isSupported;
		systemSupported.sunDepthShader = sunDepthShader.isSupported;
		systemSupported.voxelizationShader = voxelizationShader.isSupported;
		systemSupported.tracingShader = voxelTracingShader.isSupported;

		if (!systemSupported.fullFunctionality)
		{
			Debug.LogWarning("SEGI is not supported on the current platform. Check for shader compile errors in SEGI/Resources");
			enabled = false;
		}
	}

	void OnDrawGizmosSelected()
	{
		Color prevColor = Gizmos.color;
		Gizmos.color = new Color(1.0f, 0.25f, 0.0f, 0.5f);

		Gizmos.DrawCube(voxelSpaceOrigin, new Vector3(voxelSpaceSize, voxelSpaceSize, voxelSpaceSize));

		Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.1f);

		Gizmos.color = prevColor;
	}

	void CleanupTexture(ref RenderTexture texture)
	{
		texture.DiscardContents();
		DestroyImmediate(texture);
	}

	void CleanupTextures()
	{
		CleanupTexture(ref sunDepthTexture);
		CleanupTexture(ref previousResult);
		CleanupTexture(ref previousDepth);
		CleanupTexture(ref intTex1);
		for (int i = 0; i < volumeTextures.Length; i++)
		{
			CleanupTexture(ref volumeTextures[i]);
		}
		CleanupTexture(ref volumeTexture1);
		CleanupTexture(ref volumeTextureB);
		CleanupTexture(ref dummyVoxelTexture);
		CleanupTexture(ref dummyVoxelTexture2);
	}

	void Cleanup()
	{
		DestroyImmediate(material);
		DestroyImmediate(voxelCameraGO);
		DestroyImmediate(leftViewPoint);
		DestroyImmediate(topViewPoint);
		DestroyImmediate(shadowCamGameObject);
		initChecker = null;

		CleanupTextures();
	}

	void OnEnable()
	{
		InitCheck();
		ResizeRenderTextures();

		CheckSupport();
	}

	void OnDisable()
	{
		Cleanup();
	}

	void ResizeRenderTextures()
	{
		if (previousResult)
		{
			previousResult.DiscardContents();
			previousResult.Release();
			DestroyImmediate(previousResult);
		}

		int width = attachedCamera.pixelWidth == 0 ? 2 : attachedCamera.pixelWidth;
		int height = attachedCamera.pixelHeight == 0 ? 2 : attachedCamera.pixelHeight;

		previousResult = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
		previousResult.wrapMode = TextureWrapMode.Clamp;
		previousResult.filterMode = FilterMode.Bilinear;
		previousResult.useMipMap = true;
		#if UNITY_5_4_OR_NEWER
		previousResult.autoGenerateMips = false;
		#else
		previousResult.generateMips = false;
		#endif
		previousResult.Create();
		previousResult.hideFlags = HideFlags.HideAndDontSave;

		if (previousDepth)
		{
			previousDepth.DiscardContents();
			previousDepth.Release();
			DestroyImmediate(previousDepth);
		}
		previousDepth = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
		previousDepth.wrapMode = TextureWrapMode.Clamp;
		previousDepth.filterMode = FilterMode.Bilinear;
		previousDepth.Create();
		previousDepth.hideFlags = HideFlags.HideAndDontSave;
	}

	void ResizeSunShadowBuffer()
	{

		if (sunDepthTexture)
		{
			sunDepthTexture.DiscardContents();
			sunDepthTexture.Release();
			DestroyImmediate(sunDepthTexture);
		}
		sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
		sunDepthTexture.wrapMode = TextureWrapMode.Clamp;
		sunDepthTexture.filterMode = FilterMode.Point;
		sunDepthTexture.Create();
		sunDepthTexture.hideFlags = HideFlags.HideAndDontSave;
	}

	void Update()
	{
		if (dontTry)
			return;

		if (previousResult == null)
		{
			ResizeRenderTextures();
		}

		if (previousResult.width != attachedCamera.pixelWidth || previousResult.height != attachedCamera.pixelHeight)
		{
			ResizeRenderTextures();
		}

		if ((int)sunShadowResolution != prevSunShadowResolution)
		{
			ResizeSunShadowBuffer();
		}

		prevSunShadowResolution = (int)sunShadowResolution;

		if (volumeTextures[0].width != (int)voxelResolution)
		{
			CreateVolumeTextures();
		}

		if (dummyVoxelTexture.width != dummyVoxelResolution)
		{
			ResizeDummyTexture();
		}
	}

    Matrix4x4 TransformViewMatrix(Matrix4x4 mat)
    {
#if UNITY_5_5_OR_NEWER
        if (SystemInfo.usesReversedZBuffer)
        {
            mat[2, 0] = -mat[2, 0];
            mat[2, 1] = -mat[2, 1];
            mat[2, 2] = -mat[2, 2];
            mat[2, 3] = -mat[2, 3];
           // mat[3, 2] += 0.0f;
        }
#endif
        return mat;
    }

	void OnPreRender()
	{
		InitCheck();

		if (dontTry)
			return;

		if (!updateGI)
		{
			return;
		}

		RenderTexture previousActive = RenderTexture.active;

		Shader.SetGlobalInt("SEGIVoxelAA", voxelAA ? 1 : 0);

		if (renderState == RenderState.Voxelize)
		{
			activeVolume = voxelFlipFlop == 0 ? volumeTextures[0] : volumeTextureB;
			previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

			float voxelTexel = (1.0f * voxelSpaceSize) / (int)voxelResolution * 0.5f;

			float interval = voxelSpaceSize / 8.0f;
			Vector3 origin;
			if (followTransform)
			{
				origin = followTransform.position;
			}
			else
			{
				origin = transform.position + transform.forward * voxelSpaceSize / 4.0f;
			}
			voxelSpaceOrigin = new Vector3(Mathf.Round(origin.x / interval) * interval, Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval) + new Vector3(1.0f, 1.0f, 1.0f) * ((float)voxelFlipFlop * 2.0f - 1.0f) * voxelTexel * 0.0f;

			voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
			Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", voxelSpaceOriginDelta / voxelSpaceSize);

			previousVoxelSpaceOrigin = voxelSpaceOrigin;



			Shader.SetGlobalMatrix("WorldToGI", shadowCam.worldToCameraMatrix);
			Shader.SetGlobalMatrix("GIToWorld", shadowCam.cameraToWorldMatrix);
			Shader.SetGlobalMatrix("GIProjection", shadowCam.projectionMatrix);
			Shader.SetGlobalMatrix("GIProjectionInverse", shadowCam.projectionMatrix.inverse);
			Shader.SetGlobalMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
			Shader.SetGlobalFloat("GIDepthRatio", shadowSpaceDepthRatio);

			Shader.SetGlobalColor("GISunColor", sun == null ? Color.black : new Color(Mathf.Pow(sun.color.r, 2.2f), Mathf.Pow(sun.color.g, 2.2f), Mathf.Pow(sun.color.b, 2.2f), Mathf.Pow(sun.intensity, 2.2f)));
			Shader.SetGlobalColor("SEGISkyColor", new Color(Mathf.Pow(skyColor.r * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.g * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.b * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.a, 2.2f)));
			Shader.SetGlobalFloat("GIGain", giGain);


			if (sun != null)
			{
				shadowCam.cullingMask = giCullingMask;

				Vector3 shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(-sun.transform.forward) * shadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

				shadowCamTransform.position = shadowCamPosition;
				shadowCamTransform.LookAt(voxelSpaceOrigin, Vector3.up);

				shadowCam.renderingPath = RenderingPath.Forward;
				shadowCam.depthTextureMode |= DepthTextureMode.None;

				shadowCam.orthographicSize = shadowSpaceSize;
				shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;


				Graphics.SetRenderTarget(sunDepthTexture);
				shadowCam.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);

				shadowCam.RenderWithShader(sunDepthShader, "");

				Shader.SetGlobalTexture("SEGISunDepth", sunDepthTexture);
			}


			voxelCamera.enabled = false;
			voxelCamera.orthographic = true;
			voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
			voxelCamera.nearClipPlane = 0.0f;
			voxelCamera.farClipPlane = voxelSpaceSize;
			voxelCamera.depth = -2;
			voxelCamera.renderingPath = RenderingPath.Forward;
			voxelCamera.clearFlags = CameraClearFlags.Color;
			voxelCamera.backgroundColor = Color.black;
			voxelCamera.cullingMask = giCullingMask;



			voxelFlipFlop += 1;
			voxelFlipFlop = voxelFlipFlop % 2;

			voxelCameraGO.transform.position = voxelSpaceOrigin - Vector3.forward * voxelSpaceSize * 0.5f;
			voxelCameraGO.transform.rotation = rotationFront;

			leftViewPoint.transform.position = voxelSpaceOrigin + Vector3.left * voxelSpaceSize * 0.5f;
			leftViewPoint.transform.rotation = rotationLeft;
			topViewPoint.transform.position = voxelSpaceOrigin + Vector3.up * voxelSpaceSize * 0.5f;
			topViewPoint.transform.rotation = rotationTop;

			Shader.SetGlobalInt("SEGIVoxelResolution", (int)voxelResolution);

			Shader.SetGlobalMatrix("SEGIVoxelViewFront", TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
			Shader.SetGlobalMatrix("SEGIVoxelViewLeft", TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
			Shader.SetGlobalMatrix("SEGIVoxelViewTop", TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
			Shader.SetGlobalMatrix("SEGIWorldToVoxel", voxelCamera.worldToCameraMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelProjection", voxelCamera.projectionMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelProjectionInverse", voxelCamera.projectionMatrix.inverse);



			Shader.SetGlobalFloat("SEGISecondaryBounceGain", infiniteBounces ? secondaryBounceGain : 0.0f);
			Shader.SetGlobalFloat("SEGISoftSunlight", softSunlight);
			Shader.SetGlobalInt("SEGISphericalSkylight", sphericalSkylight ? 1 : 0);
			Shader.SetGlobalInt("SEGIInnerOcclusionLayers", innerOcclusionLayers);

			clearCompute.SetTexture(0, "RG0", intTex1);
			//clearCompute.SetTexture(0, "BA0", ba0);
			clearCompute.SetInt("Res", (int)voxelResolution);
			clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			Matrix4x4 voxelToGIProjection = (shadowCam.projectionMatrix) * (shadowCam.worldToCameraMatrix) * (voxelCamera.cameraToWorldMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);
			Shader.SetGlobalVector("SEGISunlightVector", sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);


			Graphics.SetRandomWriteTarget(1, intTex1);
			voxelCamera.targetTexture = dummyVoxelTexture;
			voxelCamera.RenderWithShader(voxelizationShader, "");
			Graphics.ClearRandomWriteTargets();

			transferInts.SetTexture(0, "Result", activeVolume);
			transferInts.SetTexture(0, "PrevResult", previousActiveVolume);
			transferInts.SetTexture(0, "RG0", intTex1);
			transferInts.SetInt("VoxelAA", voxelAA ? 1 : 0);
			transferInts.SetInt("Resolution", (int)voxelResolution);
			transferInts.SetVector("VoxelOriginDelta", (voxelSpaceOriginDelta / voxelSpaceSize) * (int)voxelResolution);
			transferInts.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			Shader.SetGlobalTexture("SEGIVolumeLevel0", activeVolume);

			for (int i = 0; i < numMipLevels - 1; i++)
			{
				RenderTexture source = volumeTextures[i];

				if (i == 0)
				{
					source = activeVolume;
				}

				int destinationRes = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i + 1.0f));
				mipFilter.SetInt("destinationRes", destinationRes);
				mipFilter.SetTexture(mipFilterKernel, "Source", source);
				mipFilter.SetTexture(mipFilterKernel, "Destination", volumeTextures[i + 1]);
				mipFilter.Dispatch(mipFilterKernel, destinationRes / 8, destinationRes / 8, 1);
				Shader.SetGlobalTexture("SEGIVolumeLevel" + (i + 1).ToString(), volumeTextures[i + 1]);
			}

			if (infiniteBounces)
			{
				renderState = RenderState.Bounce;
			}
		}
		else if (renderState == RenderState.Bounce)
		{

			clearCompute.SetTexture(0, "RG0", intTex1);
			clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			Shader.SetGlobalInt("SEGISecondaryCones", secondaryCones);
			Shader.SetGlobalFloat("SEGISecondaryOcclusionStrength", secondaryOcclusionStrength);

			Graphics.SetRandomWriteTarget(1, intTex1);
			voxelCamera.targetTexture = dummyVoxelTexture2;
			voxelCamera.RenderWithShader(voxelTracingShader, "");
			Graphics.ClearRandomWriteTargets();

			transferInts.SetTexture(1, "Result", volumeTexture1);
			transferInts.SetTexture(1, "RG0", intTex1);
			transferInts.SetInt("Resolution", (int)voxelResolution);
			transferInts.Dispatch(1, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			Shader.SetGlobalTexture("SEGIVolumeTexture1", volumeTexture1);

			renderState = RenderState.Voxelize;
		}
		Matrix4x4 giToVoxelProjection = voxelCamera.projectionMatrix * voxelCamera.worldToCameraMatrix * shadowCam.cameraToWorldMatrix;
		Shader.SetGlobalMatrix("GIToVoxelProjection", giToVoxelProjection);



		RenderTexture.active = previousActive;
	}

	[ImageEffectOpaque]
	void OnRenderImage(RenderTexture source, RenderTexture destination)
	{
		if (dontTry)
		{
			Graphics.Blit(source, destination);
			return;
		}

		Shader.SetGlobalFloat("SEGIVoxelScaleFactor", voxelScaleFactor);

		material.SetMatrix("CameraToWorld", attachedCamera.cameraToWorldMatrix);
		material.SetMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
		material.SetMatrix("ProjectionMatrixInverse", attachedCamera.projectionMatrix.inverse);
		material.SetMatrix("ProjectionMatrix", attachedCamera.projectionMatrix);
		material.SetInt("FrameSwitch", frameSwitch);
		Shader.SetGlobalInt("SEGIFrameSwitch", frameSwitch);
		material.SetVector("CameraPosition", transform.position);
		material.SetFloat("DeltaTime", Time.deltaTime);

		material.SetInt("StochasticSampling", stochasticSampling ? 1 : 0);
		material.SetInt("TraceDirections", cones);
		material.SetInt("TraceSteps", coneTraceSteps);
		material.SetFloat("TraceLength", coneLength);
		material.SetFloat("ConeSize", coneWidth);
		material.SetFloat("OcclusionStrength", occlusionStrength);
		material.SetFloat("OcclusionPower", occlusionPower);
		material.SetFloat("ConeTraceBias", coneTraceBias);
		material.SetFloat("GIGain", giGain);
		material.SetFloat("NearLightGain", nearLightGain);
		material.SetFloat("NearOcclusionStrength", nearOcclusionStrength);
		material.SetInt("DoReflections", doReflections ? 1 : 0);
		material.SetInt("HalfResolution", halfResolution ? 1 : 0);
		material.SetInt("ReflectionSteps", reflectionSteps);
		material.SetFloat("ReflectionOcclusionPower", reflectionOcclusionPower);
		material.SetFloat("SkyReflectionIntensity", skyReflectionIntensity);
		material.SetFloat("FarOcclusionStrength", farOcclusionStrength);
		material.SetFloat("FarthestOcclusionStrength", farthestOcclusionStrength);
        material.SetTexture("NoiseTexture", blueNoise[frameSwitch % 64]);

		if (visualizeVoxels)
		{
			Graphics.Blit(source, destination, material, Pass.VisualizeVoxels);
			return;
		}

		RenderTexture gi1 = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf);
		RenderTexture gi2 = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf);
		RenderTexture reflections = null;

		if (doReflections)
		{
			reflections = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
		}

		RenderTexture currentDepth = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
		currentDepth.filterMode = FilterMode.Point;

		RenderTexture currentNormal = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		currentNormal.filterMode = FilterMode.Point;

		Graphics.Blit(source, currentDepth, material, Pass.GetCameraDepthTexture);
		material.SetTexture("CurrentDepth", currentDepth);
		Graphics.Blit(source, currentNormal, material, Pass.GetWorldNormals);
		material.SetTexture("CurrentNormal", currentNormal);

		material.SetTexture("PreviousGITexture", previousResult);
		Shader.SetGlobalTexture("PreviousGITexture", previousResult);
		material.SetTexture("PreviousDepth", previousDepth);


		Graphics.Blit(source, gi2, material, Pass.DiffuseTrace);
		if (doReflections)
		{
			Graphics.Blit(source, reflections, material, Pass.SpecularTrace);
			material.SetTexture("Reflections", reflections);
		}

		material.SetFloat("BlendWeight", temporalBlendWeight);


		if (useBilateralFiltering)
		{
			material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
			Graphics.Blit(gi2, gi1, material, Pass.BilateralBlur);

			material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
			Graphics.Blit(gi1, gi2, material, Pass.BilateralBlur);

			material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
			Graphics.Blit(gi2, gi1, material, Pass.BilateralBlur);

			material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
			Graphics.Blit(gi1, gi2, material, Pass.BilateralBlur);
		}

		if (giRenderRes == 2)
		{
			RenderTexture.ReleaseTemporary(gi1);


			RenderTexture gi3 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
			RenderTexture gi4 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);

			gi2.filterMode = FilterMode.Point;
			Graphics.Blit(gi2, gi4);

			RenderTexture.ReleaseTemporary(gi2);

			gi4.filterMode = FilterMode.Point;
			gi3.filterMode = FilterMode.Point;

			material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
			Graphics.Blit(gi4, gi3, material, Pass.BilateralUpsample);
			material.SetVector("Kernel", new Vector2(0.0f, 1.0f));

			if (temporalBlendWeight < 1.0f)
			{
				Graphics.Blit(gi3, gi4);
				Graphics.Blit(gi4, gi3, material, Pass.TemporalBlend);
				Graphics.Blit(gi3, previousResult);
				Graphics.Blit(source, previousDepth, material, Pass.GetCameraDepthTexture);
			}

			material.SetTexture("GITexture", gi3);

			Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);

			RenderTexture.ReleaseTemporary(gi3);
			RenderTexture.ReleaseTemporary(gi4);
		}
		else
		{
			if (temporalBlendWeight < 1.0f)
			{
				Graphics.Blit(gi2, gi1, material, Pass.TemporalBlend);
				Graphics.Blit(gi1, previousResult);
				Graphics.Blit(source, previousDepth, material, Pass.GetCameraDepthTexture);
			}

			material.SetTexture("GITexture", temporalBlendWeight < 1.0f ? gi1 : gi2);
			Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);

			RenderTexture.ReleaseTemporary(gi1);
			RenderTexture.ReleaseTemporary(gi2);
		}

		RenderTexture.ReleaseTemporary(currentDepth);
		RenderTexture.ReleaseTemporary(currentNormal);

		if (visualizeSunDepthTexture)
			Graphics.Blit(sunDepthTexture, destination);


		if (doReflections)
		{
			RenderTexture.ReleaseTemporary(reflections);
		}

		material.SetMatrix("ProjectionPrev", attachedCamera.projectionMatrix);
		material.SetMatrix("ProjectionPrevInverse", attachedCamera.projectionMatrix.inverse);
		material.SetMatrix("WorldToCameraPrev", attachedCamera.worldToCameraMatrix);
		material.SetMatrix("CameraToWorldPrev", attachedCamera.cameraToWorldMatrix);
		material.SetVector("CameraPositionPrev", transform.position);

		frameSwitch = (frameSwitch + 1) % (64);
	}
}

