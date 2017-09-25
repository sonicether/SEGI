using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;


[ExecuteInEditMode]
#if UNITY_5_4_OR_NEWER
[ImageEffectAllowedInSceneView]
#endif
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Sonic Ether/SEGI")]
public class SEGI : MonoBehaviour
{

#region Parameters
	[Serializable]
	public enum VoxelResolution
	{
		low = 128,
		high = 256
	}

	public bool updateGI = true;
	public LayerMask giCullingMask = 2147483647;
	public float shadowSpaceSize = 50.0f;
	public Light sun;

	public Color skyColor;

	public float voxelSpaceSize = 25.0f;

	public bool useBilateralFiltering = false;

	[Range(0, 2)]
	public int innerOcclusionLayers = 1;


	[Range(0.01f, 1.0f)]
	public float temporalBlendWeight = 0.1f;


	public VoxelResolution voxelResolution = VoxelResolution.high;

	public bool visualizeSunDepthTexture = false;
	public bool visualizeGI = false;
	public bool visualizeVoxels = false;

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

	public bool voxelAA = false;

	public bool gaussianMipFilter = false;


	[Range(0.1f, 4.0f)]
	public float farOcclusionStrength = 1.0f;
	[Range(0.1f, 4.0f)]
	public float farthestOcclusionStrength = 1.0f;

	[Range(3, 16)]
	public int secondaryCones = 6;
	[Range(0.1f, 4.0f)]
	public float secondaryOcclusionStrength = 1.0f;

	public bool sphericalSkylight = false;

#endregion






#region InternalVariables
	object initChecker;
	Material material;
	Camera attachedCamera;
	Transform shadowCamTransform;
	Camera shadowCam;
	GameObject shadowCamGameObject;
    Texture2D[] blueNoise;
	
	int sunShadowResolution = 256;
	int prevSunShadowResolution;

	Shader sunDepthShader;

	float shadowSpaceDepthRatio = 10.0f;

	int frameCounter = 0;


	RenderTexture sunDepthTexture;
	RenderTexture previousGIResult;
	RenderTexture previousCameraDepth;

	///<summary>This is a volume texture that is immediately written to in the voxelization shader. The RInt format enables atomic writes to avoid issues where multiple fragments are trying to write to the same voxel in the volume.</summary>
	RenderTexture integerVolume;

	///<summary>An array of volume textures where each element is a mip/LOD level. Each volume is half the resolution of the previous volume. Separate textures for each mip level are required for manual mip-mapping of the main GI volume texture.</summary>
	RenderTexture[] volumeTextures;

	///<summary>The secondary volume texture that holds irradiance calculated during the in-volume GI tracing that occurs when Infinite Bounces is enabled. </summary>
	RenderTexture secondaryIrradianceVolume;

	///<summary>The alternate mip level 0 main volume texture needed to avoid simultaneous read/write errors while performing temporal stabilization on the main voxel volume.</summary>
	RenderTexture volumeTextureB;

	///<summary>The current active volume texture that holds GI information to be read during GI tracing.</summary>
	RenderTexture activeVolume;

	///<summary>The volume texture that holds GI information to be read during GI tracing that was used in the previous frame.</summary>
	RenderTexture previousActiveVolume;

	///<summary>A 2D texture with the size of [voxel resolution, voxel resolution] that must be used as the active render texture when rendering the scene for voxelization. This texture scales depending on whether Voxel AA is enabled to ensure correct voxelization.</summary>
	RenderTexture dummyVoxelTextureAAScaled;

	///<summary>A 2D texture with the size of [voxel resolution, voxel resolution] that must be used as the active render texture when rendering the scene for voxelization. This texture is always the same size whether Voxel AA is enabled or not.</summary>
	RenderTexture dummyVoxelTextureFixed;

	bool notReadyToRender = false;

	Shader voxelizationShader;
	Shader voxelTracingShader;

	ComputeShader clearCompute;
	ComputeShader transferIntsCompute;
	ComputeShader mipFilterCompute;

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

	
	enum RenderState
	{
		Voxelize,
		Bounce
	}

	RenderState renderState = RenderState.Voxelize;
#endregion





#region SupportingObjectsAndProperties
	struct Pass
	{
		public static int DiffuseTrace = 0;
		public static int BilateralBlur = 1;
		public static int BlendWithScene = 2;
		public static int TemporalBlend = 3;
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

			if (previousGIResult != null)
				v += previousGIResult.width * previousGIResult.height * 16 * 4;

			if (previousCameraDepth != null)
				v += previousCameraDepth.width * previousCameraDepth.height * 32;

			if (integerVolume != null)
				v += integerVolume.width * integerVolume.height * integerVolume.volumeDepth * 32;

			if (volumeTextures != null)
			{
				for (int i = 0; i < volumeTextures.Length; i++)
				{
					if (volumeTextures[i] != null)
						v += volumeTextures[i].width * volumeTextures[i].height * volumeTextures[i].volumeDepth * 16 * 4;
				}
			}

			if (secondaryIrradianceVolume != null)
				v += secondaryIrradianceVolume.width * secondaryIrradianceVolume.height * secondaryIrradianceVolume.volumeDepth * 16 * 4;

			if (volumeTextureB != null)
				v += volumeTextureB.width * volumeTextureB.height * volumeTextureB.volumeDepth * 16 * 4;

			if (dummyVoxelTextureAAScaled != null)
				v += dummyVoxelTextureAAScaled.width * dummyVoxelTextureAAScaled.height * 8;

			if (dummyVoxelTextureFixed != null)
				v += dummyVoxelTextureFixed.width * dummyVoxelTextureFixed.height * 8;

			float vram = (v / 8388608.0f);

			return vram;
		}
	}

	int mipFilterKernel
	{
		get
		{
			return gaussianMipFilter ? 1 : 0;
		}
	}

	int dummyVoxelResolution
	{
		get
		{
			return (int)voxelResolution * (voxelAA ? 2 : 1);
		}
	}

	int giRenderRes
	{
		get
		{
			return halfResolution ? 2 : 1;
		}
	}

#endregion


	///<summary>Applies an SEGIPreset to this instance of SEGI.</summary>
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
		if (volumeTextures != null)
		{
			for (int i = 0; i < numMipLevels; i++)
			{
				if (volumeTextures[i] != null) {
					volumeTextures[i].DiscardContents();
					volumeTextures[i].Release();
					DestroyImmediate(volumeTextures[i]);
				}
			}			
		}
		
		volumeTextures = new RenderTexture[numMipLevels];

		for (int i = 0; i < numMipLevels; i++)
		{
			int resolution = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i));
			volumeTextures[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
			#if UNITY_5_4_OR_NEWER
			volumeTextures[i].dimension = TextureDimension.Tex3D;
			#else
			volumeTextures[i].isVolume = true;
			#endif
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
		#if UNITY_5_4_OR_NEWER
		volumeTextureB.dimension = TextureDimension.Tex3D;
		#else
		volumeTextureB.isVolume = true;
		#endif
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

		if (secondaryIrradianceVolume)
		{
			secondaryIrradianceVolume.DiscardContents();
			secondaryIrradianceVolume.Release();
			DestroyImmediate(secondaryIrradianceVolume);
		}
		secondaryIrradianceVolume = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		#if UNITY_5_4_OR_NEWER
		secondaryIrradianceVolume.dimension = TextureDimension.Tex3D;
		#else
		secondaryIrradianceVolume.isVolume = true;
		#endif
		secondaryIrradianceVolume.volumeDepth = (int)voxelResolution;
		secondaryIrradianceVolume.enableRandomWrite = true;
		secondaryIrradianceVolume.filterMode = FilterMode.Point;
		#if UNITY_5_4_OR_NEWER
		secondaryIrradianceVolume.autoGenerateMips = false;
		#else
		secondaryIrradianceVolume.generateMips = false;
		#endif
		secondaryIrradianceVolume.useMipMap = false;
		secondaryIrradianceVolume.antiAliasing = 1;
		secondaryIrradianceVolume.Create();
		secondaryIrradianceVolume.hideFlags = HideFlags.HideAndDontSave;



		if (integerVolume)
		{
			integerVolume.DiscardContents();
			integerVolume.Release();
			DestroyImmediate(integerVolume);
		}
		integerVolume = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.RInt, RenderTextureReadWrite.Linear);
		#if UNITY_5_4_OR_NEWER
		integerVolume.dimension = TextureDimension.Tex3D;
		#else
		integerVolume.isVolume = true;
		#endif
		integerVolume.volumeDepth = (int)voxelResolution;
		integerVolume.enableRandomWrite = true;
		integerVolume.filterMode = FilterMode.Point;
		integerVolume.Create();
		integerVolume.hideFlags = HideFlags.HideAndDontSave;

		ResizeDummyTexture();

	}

	void ResizeDummyTexture()
	{
		if (dummyVoxelTextureAAScaled)
		{
			dummyVoxelTextureAAScaled.DiscardContents();
			dummyVoxelTextureAAScaled.Release();
			DestroyImmediate(dummyVoxelTextureAAScaled);
		}
		dummyVoxelTextureAAScaled = new RenderTexture(dummyVoxelResolution, dummyVoxelResolution, 0, RenderTextureFormat.R8);
		dummyVoxelTextureAAScaled.Create();
		dummyVoxelTextureAAScaled.hideFlags = HideFlags.HideAndDontSave;

		if (dummyVoxelTextureFixed)
		{
			dummyVoxelTextureFixed.DiscardContents();
			dummyVoxelTextureFixed.Release();
			DestroyImmediate(dummyVoxelTextureFixed);
		}
		dummyVoxelTextureFixed = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.R8);
		dummyVoxelTextureFixed.Create();
		dummyVoxelTextureFixed.hideFlags = HideFlags.HideAndDontSave;
	}

	void Init()
	{
		//Setup shaders and materials
		sunDepthShader = Shader.Find("Hidden/SEGIRenderSunDepth");

		clearCompute = Resources.Load("SEGIClear") as ComputeShader;
		transferIntsCompute = Resources.Load("SEGITransferInts") as ComputeShader;
		mipFilterCompute = Resources.Load("SEGIMipFilter") as ComputeShader;

		voxelizationShader = Shader.Find("Hidden/SEGIVoxelizeScene");
		voxelTracingShader = Shader.Find("Hidden/SEGITraceScene");
		
		if (!material) {
			material = new Material(Shader.Find("Hidden/SEGI"));
			material.hideFlags = HideFlags.HideAndDontSave;
		}
		
		//Get the camera attached to this game object
		attachedCamera = this.GetComponent<Camera>();
		attachedCamera.depthTextureMode |= DepthTextureMode.Depth;
		#if UNITY_5_4_OR_NEWER
		attachedCamera.depthTextureMode |= DepthTextureMode.MotionVectors;
		#endif


		//Find the proxy shadow rendering camera if it exists
		GameObject scgo = GameObject.Find("SEGI_SHADOWCAM");

		//If not, create it
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
		else	//Otherwise, it already exists, just get it
		{
			shadowCamGameObject = scgo;
			shadowCam = scgo.GetComponent<Camera>();
			shadowCamTransform = shadowCamGameObject.transform;
		}


		
		//Create the proxy camera objects responsible for rendering the scene to voxelize the scene. If they already exist, destroy them
		GameObject vcgo = GameObject.Find("SEGI_VOXEL_CAMERA");
		
		if (!vcgo) {
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
		}
		else
		{
			voxelCameraGO = vcgo;
			voxelCamera = vcgo.GetComponent<Camera>();
		}

		GameObject lvp = GameObject.Find("SEGI_LEFT_VOXEL_VIEW");
		
		if (!lvp) {
			leftViewPoint = new GameObject("SEGI_LEFT_VOXEL_VIEW");
			leftViewPoint.hideFlags = HideFlags.HideAndDontSave;
		}
		else
		{
			leftViewPoint = lvp;
		}

		GameObject tvp = GameObject.Find("SEGI_TOP_VOXEL_VIEW");
		
		if (!tvp) {
			topViewPoint = new GameObject("SEGI_TOP_VOXEL_VIEW");
			topViewPoint.hideFlags = HideFlags.HideAndDontSave;
		}
		else
		{
			topViewPoint = tvp;
		}

		//Get blue noise textures
		blueNoise = null;
		blueNoise = new Texture2D[64];
		for (int i = 0; i < 64; i++)
		{
		    string fileName = "LDR_RGBA_" + i.ToString();
		    Texture2D blueNoiseTexture = Resources.Load("Noise Textures/" + fileName) as Texture2D;

		    if (blueNoiseTexture == null)
		    {
			Debug.LogWarning("Unable to find noise texture \"Assets/SEGI/Resources/Noise Textures/" + fileName + "\" for SEGI!");
		    } 

		    blueNoise[i] = blueNoiseTexture;

		}

		//Setup sun depth texture
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


		//Create the volume textures
		CreateVolumeTextures();


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
		if (!enabled)
			return;
			
		Color prevColor = Gizmos.color;
		Gizmos.color = new Color(1.0f, 0.25f, 0.0f, 0.5f);

		Gizmos.DrawCube(voxelSpaceOrigin, new Vector3(voxelSpaceSize, voxelSpaceSize, voxelSpaceSize));

		Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.1f);

		Gizmos.color = prevColor;
	}

	void CleanupTexture(ref RenderTexture texture)
	{
		if (texture)
		{
			texture.DiscardContents();
			texture.Release();
			DestroyImmediate(texture);
		}
	}

	void CleanupTextures()
	{
		CleanupTexture(ref sunDepthTexture);
		CleanupTexture(ref previousGIResult);
		CleanupTexture(ref previousCameraDepth);
		CleanupTexture(ref integerVolume);
		for (int i = 0; i < volumeTextures.Length; i++)
		{
			CleanupTexture(ref volumeTextures[i]);
		}
		CleanupTexture(ref secondaryIrradianceVolume);
		CleanupTexture(ref volumeTextureB);
		CleanupTexture(ref dummyVoxelTextureAAScaled);
		CleanupTexture(ref dummyVoxelTextureFixed);
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
		if (previousGIResult)
		{
			previousGIResult.DiscardContents();
			previousGIResult.Release();
			DestroyImmediate(previousGIResult);
		}

		int width = attachedCamera.pixelWidth == 0 ? 2 : attachedCamera.pixelWidth;
		int height = attachedCamera.pixelHeight == 0 ? 2 : attachedCamera.pixelHeight;

		previousGIResult = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
		previousGIResult.wrapMode = TextureWrapMode.Clamp;
		previousGIResult.filterMode = FilterMode.Bilinear;
		previousGIResult.useMipMap = true;
		#if UNITY_5_4_OR_NEWER
		previousGIResult.autoGenerateMips = false;
		#else
		previousResult.generateMips = false;
		#endif
		previousGIResult.Create();
		previousGIResult.hideFlags = HideFlags.HideAndDontSave;

		if (previousCameraDepth)
		{
			previousCameraDepth.DiscardContents();
			previousCameraDepth.Release();
			DestroyImmediate(previousCameraDepth);
		}
		previousCameraDepth = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
		previousCameraDepth.wrapMode = TextureWrapMode.Clamp;
		previousCameraDepth.filterMode = FilterMode.Bilinear;
		previousCameraDepth.Create();
		previousCameraDepth.hideFlags = HideFlags.HideAndDontSave;
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
		if (notReadyToRender)
			return;

		if (previousGIResult == null)
		{
			ResizeRenderTextures();
		}

		if (previousGIResult.width != attachedCamera.pixelWidth || previousGIResult.height != attachedCamera.pixelHeight)
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

		if (dummyVoxelTextureAAScaled.width != dummyVoxelResolution)
		{
			ResizeDummyTexture();
		}
	}

    Matrix4x4 TransformViewMatrix(Matrix4x4 mat)
    {
		//Since the third column of the view matrix needs to be reversed if using reversed z-buffer, do so here
#if UNITY_5_5_OR_NEWER
        if (SystemInfo.usesReversedZBuffer)
        {
            mat[2, 0] = -mat[2, 0];
            mat[2, 1] = -mat[2, 1];
            mat[2, 2] = -mat[2, 2];
            mat[2, 3] = -mat[2, 3];
        }
#endif
        return mat;
    }

	void OnPreRender()
	{
		//Force reinitialization to make sure that everything is working properly if one of the cameras was unexpectedly destroyed
		if (!voxelCamera || !shadowCam)
			initChecker = null;
			
		InitCheck();

		if (notReadyToRender)
			return;

		if (!updateGI)
		{
			return;
		}

		//Cache the previous active render texture to avoid issues with other Unity rendering going on
		RenderTexture previousActive = RenderTexture.active;

		Shader.SetGlobalInt("SEGIVoxelAA", voxelAA ? 1 : 0);



		//Main voxelization work
		if (renderState == RenderState.Voxelize)
		{
			activeVolume = voxelFlipFlop == 0 ? volumeTextures[0] : volumeTextureB;				//Flip-flopping volume textures to avoid simultaneous read and write errors in shaders
			previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

			//float voxelTexel = (1.0f * voxelSpaceSize) / (int)voxelResolution * 0.5f;			//Calculate the size of a voxel texel in world-space units



			//Setup the voxel volume origin position
			float interval = voxelSpaceSize / 8.0f;												//The interval at which the voxel volume will be "locked" in world-space
			Vector3 origin;
			if (followTransform)
			{
				origin = followTransform.position;
			}
			else
			{
				//GI is still flickering a bit when the scene view and the game view are opened at the same time
				origin = transform.position + transform.forward * voxelSpaceSize / 4.0f;
			}
			//Lock the voxel volume origin based on the interval
			voxelSpaceOrigin = new Vector3(Mathf.Round(origin.x / interval) * interval, Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval);

			//Calculate how much the voxel origin has moved since last voxelization pass. Used for scrolling voxel data in shaders to avoid ghosting when the voxel volume moves in the world
			voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
			Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", voxelSpaceOriginDelta / voxelSpaceSize);

			previousVoxelSpaceOrigin = voxelSpaceOrigin;



			//Set the voxel camera (proxy camera used to render the scene for voxelization) parameters
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


			//Move the voxel camera game object and other related objects to the above calculated voxel space origin
			voxelCameraGO.transform.position = voxelSpaceOrigin - Vector3.forward * voxelSpaceSize * 0.5f;
			voxelCameraGO.transform.rotation = rotationFront;

			leftViewPoint.transform.position = voxelSpaceOrigin + Vector3.left * voxelSpaceSize * 0.5f;
			leftViewPoint.transform.rotation = rotationLeft;
			topViewPoint.transform.position = voxelSpaceOrigin + Vector3.up * voxelSpaceSize * 0.5f;
			topViewPoint.transform.rotation = rotationTop;



			//Set matrices needed for voxelization
			Shader.SetGlobalMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelViewFront", TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
			Shader.SetGlobalMatrix("SEGIVoxelViewLeft", TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
			Shader.SetGlobalMatrix("SEGIVoxelViewTop", TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
			Shader.SetGlobalMatrix("SEGIWorldToVoxel", voxelCamera.worldToCameraMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelProjection", voxelCamera.projectionMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelProjectionInverse", voxelCamera.projectionMatrix.inverse);

			Shader.SetGlobalInt("SEGIVoxelResolution", (int)voxelResolution);

			Matrix4x4 voxelToGIProjection = (shadowCam.projectionMatrix) * (shadowCam.worldToCameraMatrix) * (voxelCamera.cameraToWorldMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);
			Shader.SetGlobalVector("SEGISunlightVector", sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);

			//Set paramteters
			Shader.SetGlobalColor("GISunColor", sun == null ? Color.black : new Color(Mathf.Pow(sun.color.r, 2.2f), Mathf.Pow(sun.color.g, 2.2f), Mathf.Pow(sun.color.b, 2.2f), Mathf.Pow(sun.intensity, 2.2f)));
			Shader.SetGlobalColor("SEGISkyColor", new Color(Mathf.Pow(skyColor.r * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.g * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.b * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.a, 2.2f)));
			Shader.SetGlobalFloat("GIGain", giGain);
			Shader.SetGlobalFloat("SEGISecondaryBounceGain", infiniteBounces ? secondaryBounceGain : 0.0f);
			Shader.SetGlobalFloat("SEGISoftSunlight", softSunlight);
			Shader.SetGlobalInt("SEGISphericalSkylight", sphericalSkylight ? 1 : 0);
			Shader.SetGlobalInt("SEGIInnerOcclusionLayers", innerOcclusionLayers);


			//Render the depth texture from the sun's perspective in order to inject sunlight with shadows during voxelization
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




			




			//Clear the volume texture that is immediately written to in the voxelization scene shader
			clearCompute.SetTexture(0, "RG0", integerVolume);
			clearCompute.SetInt("Res", (int)voxelResolution);
			clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);








			//Render the scene with the voxel proxy camera object with the voxelization shader to voxelize the scene to the volume integer texture
			Graphics.SetRandomWriteTarget(1, integerVolume);
			voxelCamera.targetTexture = dummyVoxelTextureAAScaled;
			voxelCamera.RenderWithShader(voxelizationShader, "");
			Graphics.ClearRandomWriteTargets();


			//Transfer the data from the volume integer texture to the main volume texture used for GI tracing. 
			transferIntsCompute.SetTexture(0, "Result", activeVolume);
			transferIntsCompute.SetTexture(0, "PrevResult", previousActiveVolume);
			transferIntsCompute.SetTexture(0, "RG0", integerVolume);
			transferIntsCompute.SetInt("VoxelAA", voxelAA ? 1 : 0);
			transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
			transferIntsCompute.SetVector("VoxelOriginDelta", (voxelSpaceOriginDelta / voxelSpaceSize) * (int)voxelResolution);
			transferIntsCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			Shader.SetGlobalTexture("SEGIVolumeLevel0", activeVolume);

			//Manually filter/render mip maps
			for (int i = 0; i < numMipLevels - 1; i++)
			{
				RenderTexture source = volumeTextures[i];

				if (i == 0)
				{
					source = activeVolume;
				}

				int destinationRes = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i + 1.0f));
				mipFilterCompute.SetInt("destinationRes", destinationRes);
				mipFilterCompute.SetTexture(mipFilterKernel, "Source", source);
				mipFilterCompute.SetTexture(mipFilterKernel, "Destination", volumeTextures[i + 1]);
				mipFilterCompute.Dispatch(mipFilterKernel, destinationRes / 8, destinationRes / 8, 1);
				Shader.SetGlobalTexture("SEGIVolumeLevel" + (i + 1).ToString(), volumeTextures[i + 1]);
			}

			//Advance the voxel flip flop counter
			voxelFlipFlop += 1;
			voxelFlipFlop = voxelFlipFlop % 2;

			if (infiniteBounces)
			{
				renderState = RenderState.Bounce;
			}
		}
		else if (renderState == RenderState.Bounce)
		{

			//Clear the volume texture that is immediately written to in the voxelization scene shader
			clearCompute.SetTexture(0, "RG0", integerVolume);
			clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			//Set secondary tracing parameters
			Shader.SetGlobalInt("SEGISecondaryCones", secondaryCones);
			Shader.SetGlobalFloat("SEGISecondaryOcclusionStrength", secondaryOcclusionStrength);

			//Render the scene from the voxel camera object with the voxel tracing shader to render a bounce of GI into the irradiance volume
			Graphics.SetRandomWriteTarget(1, integerVolume);
			voxelCamera.targetTexture = dummyVoxelTextureFixed;
			voxelCamera.RenderWithShader(voxelTracingShader, "");
			Graphics.ClearRandomWriteTargets();


			//Transfer the data from the volume integer texture to the irradiance volume texture. This result is added to the next main voxelization pass to create a feedback loop for infinite bounces
			transferIntsCompute.SetTexture(1, "Result", secondaryIrradianceVolume);
			transferIntsCompute.SetTexture(1, "RG0", integerVolume);
			transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
			transferIntsCompute.Dispatch(1, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			Shader.SetGlobalTexture("SEGIVolumeTexture1", secondaryIrradianceVolume);

			renderState = RenderState.Voxelize;
		}



		RenderTexture.active = previousActive;
	}

	[ImageEffectOpaque]
	void OnRenderImage(RenderTexture source, RenderTexture destination)
	{
		if (notReadyToRender)
		{
			Graphics.Blit(source, destination);
			return;
		}

		//Set parameters
		Shader.SetGlobalFloat("SEGIVoxelScaleFactor", voxelScaleFactor);

		material.SetMatrix("CameraToWorld", attachedCamera.cameraToWorldMatrix);
		material.SetMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
		material.SetMatrix("ProjectionMatrixInverse", attachedCamera.projectionMatrix.inverse);
		material.SetMatrix("ProjectionMatrix", attachedCamera.projectionMatrix);
		material.SetInt("FrameSwitch", frameCounter);
		Shader.SetGlobalInt("SEGIFrameSwitch", frameCounter);
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
        material.SetTexture("NoiseTexture", blueNoise[frameCounter % 64]);
		material.SetFloat("BlendWeight", temporalBlendWeight);

		//If Visualize Voxels is enabled, just render the voxel visualization shader pass and return
		if (visualizeVoxels)
		{
			Graphics.Blit(source, destination, material, Pass.VisualizeVoxels);
			return;
		}

		//Setup temporary textures
		RenderTexture gi1 = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf);
		RenderTexture gi2 = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf);
		RenderTexture reflections = null;

		//If reflections are enabled, create a temporary render buffer to hold them
		if (doReflections)
		{
			reflections = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
		}

		//Setup textures to hold the current camera depth and normal
		RenderTexture currentDepth = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
		currentDepth.filterMode = FilterMode.Point;

		RenderTexture currentNormal = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		currentNormal.filterMode = FilterMode.Point;

		//Get the camera depth and normals
		Graphics.Blit(source, currentDepth, material, Pass.GetCameraDepthTexture);
		material.SetTexture("CurrentDepth", currentDepth);
		Graphics.Blit(source, currentNormal, material, Pass.GetWorldNormals);
		material.SetTexture("CurrentNormal", currentNormal);

		//Set the previous GI result and camera depth textures to access them in the shader
		material.SetTexture("PreviousGITexture", previousGIResult);
		Shader.SetGlobalTexture("PreviousGITexture", previousGIResult);
		material.SetTexture("PreviousDepth", previousCameraDepth);

		//Render diffuse GI tracing result
		Graphics.Blit(source, gi2, material, Pass.DiffuseTrace);
		if (doReflections)
		{
			//Render GI reflections result
			Graphics.Blit(source, reflections, material, Pass.SpecularTrace);
			material.SetTexture("Reflections", reflections);
		}


		//Perform bilateral filtering
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

		//If Half Resolution tracing is enabled
		if (giRenderRes == 2)
		{
			RenderTexture.ReleaseTemporary(gi1);

			//Setup temporary textures
			RenderTexture gi3 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
			RenderTexture gi4 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);


			//Prepare the half-resolution diffuse GI result to be bilaterally upsampled
			gi2.filterMode = FilterMode.Point;
			Graphics.Blit(gi2, gi4);

			RenderTexture.ReleaseTemporary(gi2);

			gi4.filterMode = FilterMode.Point;
			gi3.filterMode = FilterMode.Point;


			//Perform bilateral upsampling on half-resolution diffuse GI result
			material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
			Graphics.Blit(gi4, gi3, material, Pass.BilateralUpsample);
			material.SetVector("Kernel", new Vector2(0.0f, 1.0f));

			//Perform temporal reprojection and blending
			if (temporalBlendWeight < 1.0f)
			{
				Graphics.Blit(gi3, gi4);
				Graphics.Blit(gi4, gi3, material, Pass.TemporalBlend);
				Graphics.Blit(gi3, previousGIResult);
				Graphics.Blit(source, previousCameraDepth, material, Pass.GetCameraDepthTexture);
			}

			//Set the result to be accessed in the shader
			material.SetTexture("GITexture", gi3);

			//Actually apply the GI to the scene using gbuffer data
			Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);

			//Release temporary textures
			RenderTexture.ReleaseTemporary(gi3);
			RenderTexture.ReleaseTemporary(gi4);
		}
		else 	//If Half Resolution tracing is disabled
		{	
			//Perform temporal reprojection and blending
			if (temporalBlendWeight < 1.0f)
			{
				Graphics.Blit(gi2, gi1, material, Pass.TemporalBlend);
				Graphics.Blit(gi1, previousGIResult);
				Graphics.Blit(source, previousCameraDepth, material, Pass.GetCameraDepthTexture);
			}
			
			//Actually apply the GI to the scene using gbuffer data
			material.SetTexture("GITexture", temporalBlendWeight < 1.0f ? gi1 : gi2);
			Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);

			//Release temporary textures
			RenderTexture.ReleaseTemporary(gi1);
			RenderTexture.ReleaseTemporary(gi2);
		}

		//Release temporary textures
		RenderTexture.ReleaseTemporary(currentDepth);
		RenderTexture.ReleaseTemporary(currentNormal);

		//Visualize the sun depth texture
		if (visualizeSunDepthTexture)
			Graphics.Blit(sunDepthTexture, destination);


		//Release the temporary reflections result texture
		if (doReflections)
		{
			RenderTexture.ReleaseTemporary(reflections);
		}

		//Set matrices/vectors for use during temporal reprojection
		material.SetMatrix("ProjectionPrev", attachedCamera.projectionMatrix);
		material.SetMatrix("ProjectionPrevInverse", attachedCamera.projectionMatrix.inverse);
		material.SetMatrix("WorldToCameraPrev", attachedCamera.worldToCameraMatrix);
		material.SetMatrix("CameraToWorldPrev", attachedCamera.cameraToWorldMatrix);
		material.SetVector("CameraPositionPrev", transform.position);

		//Advance the frame counter
		frameCounter = (frameCounter + 1) % (64);
	}
}

