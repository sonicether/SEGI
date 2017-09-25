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
[AddComponentMenu("Image Effects/Sonic Ether/SEGI (Cascaded)")]
public class SEGICascaded : MonoBehaviour
{



#region Parameters
	[Serializable]
	public enum VoxelResolution
	{
		low = 64,
		high = 128
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

	public float voxelSpaceSize = 25.0f;

	public bool useBilateralFiltering = false;

	[Range(0, 2)]
	public int innerOcclusionLayers = 1;

	public bool halfResolution = false;
	public bool stochasticSampling = true;
	public bool infiniteBounces = false;
	public Transform followTransform;
	[Range(1, 128)]
	public int cones = 4;
	[Range(1, 32)]
	public int coneTraceSteps = 10;
	[Range(0.1f, 2.0f)]
	public float coneLength = 1.0f;
	[Range(0.5f, 6.0f)]
	public float coneWidth = 3.9f;
	[Range(0.0f, 2.0f)]
	public float occlusionStrength = 0.15f;
	[Range(0.0f, 4.0f)]
	public float nearOcclusionStrength = 0.5f;
	[Range(0.001f, 4.0f)]
	public float occlusionPower = 0.65f;
	[Range(0.0f, 4.0f)]
	public float coneTraceBias = 2.8f;
	[Range(0.0f, 4.0f)]
	public float nearLightGain = 0.36f;
	[Range(0.0f, 4.0f)]
	public float giGain = 1.0f;
	[Range(0.0f, 4.0f)]
	public float secondaryBounceGain = 0.9f;
	[Range(0.0f, 16.0f)]
	public float softSunlight = 0.0f;

	[Range(0.0f, 8.0f)]
	public float skyIntensity = 1.0f;

	[HideInInspector]
	public bool doReflections
	{
		get
		{
			return false;	//Locked to keep reflections disabled since they're in a broken state with cascades at the moment
		}
		set
		{
			value = false;
		}
	}

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
	[Range(0.1f, 2.0f)]
	public float secondaryOcclusionStrength = 0.27f;

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

	int sunShadowResolution = 128;
	int prevSunShadowResolution;

	Shader sunDepthShader;

	float shadowSpaceDepthRatio = 10.0f;

	int frameCounter = 0;



	RenderTexture sunDepthTexture;
	RenderTexture previousGIResult;
	RenderTexture previousDepth;

	///<summary>This is a volume texture that is immediately written to in the voxelization shader. The RInt format enables atomic writes to avoid issues where multiple fragments are trying to write to the same voxel in the volume.</summary>
	RenderTexture integerVolume;

	///<summary>A 2D texture with the size of [voxel resolution, voxel resolution] that must be used as the active render texture when rendering the scene for voxelization. This texture scales depending on whether Voxel AA is enabled to ensure correct voxelization.</summary>
	RenderTexture dummyVoxelTextureAAScaled;

	///<summary>A 2D texture with the size of [voxel resolution, voxel resolution] that must be used as the active render texture when rendering the scene for voxelization. This texture is always the same size whether Voxel AA is enabled or not.</summary>
	RenderTexture dummyVoxelTextureFixed;

	///<summary>The main GI data clipmaps that hold GI data referenced during GI tracing</summary>
	Clipmap[] clipmaps;

	///<summary>The secondary clipmaps that hold irradiance data for infinite bounces</summary>
	Clipmap[] irradianceClipmaps;



	bool notReadyToRender = false;

	Shader voxelizationShader;
	Shader voxelTracingShader;

	ComputeShader clearCompute;
	ComputeShader transferIntsCompute;
	ComputeShader mipFilterCompute;

	const int numClipmaps = 6;
	int clipmapCounter = 0;
	int currentClipmapIndex = 0;

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

	Quaternion rotationFront = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
	Quaternion rotationLeft = new Quaternion(0.0f, 0.7f, 0.0f, 0.7f);
	Quaternion rotationTop = new Quaternion(0.7f, 0.0f, 0.0f, 0.7f);

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
	public float vramUsage	//TODO: Update vram usage calculation
	{
		get
		{
			if (!enabled)
			{
				return 0.0f;
			}
			long v = 0;

			if (sunDepthTexture != null)
				v += sunDepthTexture.width * sunDepthTexture.height * 16;

			if (previousGIResult != null)
				v += previousGIResult.width * previousGIResult.height * 16 * 4;

			if (previousDepth != null)
				v += previousDepth.width * previousDepth.height * 32;

			if (integerVolume != null)
				v += integerVolume.width * integerVolume.height * integerVolume.volumeDepth * 32;

			if (dummyVoxelTextureAAScaled != null)
				v += dummyVoxelTextureAAScaled.width * dummyVoxelTextureAAScaled.height * 8;

			if (dummyVoxelTextureFixed != null)
				v += dummyVoxelTextureFixed.width * dummyVoxelTextureFixed.height * 8;

			if (clipmaps != null)
			{
				for (int i = 0; i < numClipmaps; i++)
				{
					if (clipmaps[i] != null)
					{
						v += clipmaps[i].volumeTexture0.width * clipmaps[i].volumeTexture0.height * clipmaps[i].volumeTexture0.volumeDepth * 16 * 4;
					}
				}
			}
			
			if (irradianceClipmaps != null)
			{
				for (int i = 0; i < numClipmaps; i++)
				{
					if (irradianceClipmaps[i] != null)
					{
						v += irradianceClipmaps[i].volumeTexture0.width * irradianceClipmaps[i].volumeTexture0.height * irradianceClipmaps[i].volumeTexture0.volumeDepth * 16 * 4;
					}
				}
			}	

			float vram = (v / 8388608.0f);

			return vram;
		}
	}

	class Clipmap
	{
		public Vector3 origin;
		public Vector3 originDelta;
		public Vector3 previousOrigin;
		public float localScale;

		public int resolution;

		public RenderTexture volumeTexture0;

		public FilterMode filterMode = FilterMode.Bilinear;
		public RenderTextureFormat renderTextureFormat = RenderTextureFormat.ARGBHalf;

		public void UpdateTextures()
		{
			if (volumeTexture0)
			{
				volumeTexture0.DiscardContents();
				volumeTexture0.Release();
				DestroyImmediate(volumeTexture0);
			}
			volumeTexture0 = new RenderTexture(resolution, resolution, 0, renderTextureFormat, RenderTextureReadWrite.Linear);
			volumeTexture0.wrapMode = TextureWrapMode.Clamp;
			#if UNITY_5_4_OR_NEWER
			volumeTexture0.dimension = TextureDimension.Tex3D;
			#else
			volumeTexture0.isVolume = true;
			#endif
			volumeTexture0.volumeDepth = resolution;
			volumeTexture0.enableRandomWrite = true;
			volumeTexture0.filterMode = filterMode;
			#if UNITY_5_4_OR_NEWER
			volumeTexture0.autoGenerateMips = false;
			#else
			volumeTexture0.generateMips = false;
			#endif
			volumeTexture0.useMipMap = false;
			volumeTexture0.Create();
			volumeTexture0.hideFlags = HideFlags.HideAndDontSave;
		}

		public void CleanupTextures()
		{
			if (volumeTexture0)
			{
				volumeTexture0.DiscardContents();
				volumeTexture0.Release();
				DestroyImmediate(volumeTexture0);
			}
		}
	}

	public bool gaussianMipFilter
	{
		get
		{
			return false;
		}
		set
		{
			value = false;
		}
	}

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
			return (int)voxelResolution * (voxelAA ? 4 : 1);
		}
	}

#endregion



	public void ApplyPreset(SEGICascadedPreset preset)
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

	void BuildClipmaps()
	{
		if (clipmaps != null)
		{
			for (int i = 0; i < numClipmaps; i++)
			{
				if (clipmaps[i] != null)
				{
					clipmaps[i].CleanupTextures();
				}
			}
		}
		
		clipmaps = new Clipmap[numClipmaps];

		for (int i = 0; i < numClipmaps; i++)
		{
			clipmaps[i] = new Clipmap();
			clipmaps[i].localScale = Mathf.Pow(2.0f, (float)i);
			clipmaps[i].resolution = (int)voxelResolution;
			clipmaps[i].filterMode = FilterMode.Bilinear;
			clipmaps[i].renderTextureFormat = RenderTextureFormat.ARGBHalf;
			clipmaps[i].UpdateTextures();
		}

		if (irradianceClipmaps != null)
		{
			for (int i = 0; i < numClipmaps; i++)
			{
				if (irradianceClipmaps[i] != null)
				{
					irradianceClipmaps[i].CleanupTextures();
				}
			}
		}
		
		irradianceClipmaps = new Clipmap[numClipmaps];

		for (int i = 0; i < numClipmaps; i++)
		{
			irradianceClipmaps[i] = new Clipmap();
			irradianceClipmaps[i].localScale = Mathf.Pow(2.0f, i);
			irradianceClipmaps[i].resolution = (int)voxelResolution;
			irradianceClipmaps[i].filterMode = FilterMode.Point;
			irradianceClipmaps[i].renderTextureFormat = RenderTextureFormat.ARGBHalf;
			irradianceClipmaps[i].UpdateTextures();
		}
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

	void GetBlueNoiseTextures()
	{
		blueNoise = null;
		blueNoise = new Texture2D[64];
		for (int i = 0; i < 64; i++)
		{
			string filename = "LDR_RGBA_" + i.ToString();
			Texture2D blueNoiseTexture = Resources.Load("Noise Textures/" + filename) as Texture2D;

			if (blueNoiseTexture == null)
			{
				Debug.LogWarning("Unable to find noise texture \"Assets/SEGI/Resources/Noise Textures/" + filename + "\" for SEGI!");
			}

			blueNoise[i] = blueNoiseTexture;
		}
	}

	void Init()
	{
		//Setup shaders and materials
		sunDepthShader = Shader.Find("Hidden/SEGIRenderSunDepth_C");
		clearCompute = Resources.Load("SEGIClear_C") as ComputeShader;
		transferIntsCompute = Resources.Load("SEGITransferInts_C") as ComputeShader;
		mipFilterCompute = Resources.Load("SEGIMipFilter_C") as ComputeShader;
		voxelizationShader = Shader.Find("Hidden/SEGIVoxelizeScene_C");
		voxelTracingShader = Shader.Find("Hidden/SEGITraceScene_C");

		if (!material) {
			material = new Material(Shader.Find("Hidden/SEGI_C"));
			material.hideFlags = HideFlags.HideAndDontSave;
		}

		//Get the camera attached to this game object
		attachedCamera = this.GetComponent<Camera>();
		attachedCamera.depthTextureMode |= DepthTextureMode.Depth;
		attachedCamera.depthTextureMode |= DepthTextureMode.DepthNormals;
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



		CreateVolumeTextures();
		BuildClipmaps();
		GetBlueNoiseTextures();



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

		float scale = clipmaps[numClipmaps - 1].localScale;
		Gizmos.DrawCube(clipmaps[0].origin, new Vector3(voxelSpaceSize * scale, voxelSpaceSize * scale, voxelSpaceSize * scale));

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
		CleanupTexture(ref previousDepth);
		CleanupTexture(ref integerVolume);
		CleanupTexture(ref dummyVoxelTextureAAScaled);
		CleanupTexture(ref dummyVoxelTextureFixed);

		if (clipmaps != null)
		{
			for (int i = 0; i < numClipmaps; i++)
			{
				if (clipmaps[i] != null)
				{
					clipmaps[i].CleanupTextures();
				}
			}
		}
				
		if (irradianceClipmaps != null)
		{
			for (int i = 0; i < numClipmaps; i++)
			{
				if (irradianceClipmaps[i] != null)
				{
					irradianceClipmaps[i].CleanupTextures();
				}
			}
		}
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
		previousGIResult.Create();
		previousGIResult.hideFlags = HideFlags.HideAndDontSave;

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

		if (clipmaps[0].resolution != (int)voxelResolution)
		{
			clipmaps[0].resolution = (int)voxelResolution;
			clipmaps[0].UpdateTextures();
		}

		if (dummyVoxelTextureAAScaled.width != dummyVoxelResolution)
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

	int SelectCascadeBinary(int c)
	{
		float counter = c + 0.01f;

		int result = 0;
		for (int i = 1; i < numClipmaps; i++)
		{
			float level = Mathf.Pow(2.0f, i);
			result += Mathf.CeilToInt(	((counter / level) % 1.0f) - ((level - 1.0f) / level)	);		
		}

		return result;
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

		Shader.SetGlobalInt("SEGIVoxelAA", voxelAA ? 3 : 0);

		//Temporarily disable rendering of shadows on the directional light during voxelization pass. Cache the result to set it back to what it was after voxelization is done
        LightShadows prevSunShadowSetting = LightShadows.None;
		if (sun != null)
		{
			prevSunShadowSetting = sun.shadows;
        	sun.shadows = LightShadows.None;
		}

		//Main voxelization work
		if (renderState == RenderState.Voxelize)
		{
			currentClipmapIndex = SelectCascadeBinary(clipmapCounter);		//Determine which clipmap to update during this frame

			Clipmap activeClipmap = clipmaps[currentClipmapIndex];			//Set the active clipmap based on which one is determined to render this frame

			//If we're not updating the base level 0 clipmap, get the previous clipmap
			Clipmap prevClipmap = null;
			if (currentClipmapIndex != 0)
			{
				prevClipmap = clipmaps[currentClipmapIndex - 1];
            }

            float clipmapShadowSize = shadowSpaceSize * activeClipmap.localScale;
            float clipmapSize = voxelSpaceSize * activeClipmap.localScale;	//Determine the current clipmap's size in world units based on its scale
			//float voxelTexel = (1.0f * clipmapSize) / activeClipmap.resolution * 0.5f;	//Calculate the size of a voxel texel in world-space units





			//Setup the voxel volume origin position
			float interval = (clipmapSize) / 8.0f;							//The interval at which the voxel volume will be "locked" in world-space
			Vector3 origin;
			if (followTransform)
			{
				origin = followTransform.position;
			}
			else
			{
				//GI is still flickering a bit when the scene view and the game view are opened at the same time
				origin = transform.position + transform.forward * clipmapSize / 4.0f;
			}
			//Lock the voxel volume origin based on the interval
			activeClipmap.previousOrigin = activeClipmap.origin;
			activeClipmap.origin = new Vector3(Mathf.Round(origin.x / interval) * interval, Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval);


			//Clipmap delta movement for scrolling secondary bounce irradiance volume when this clipmap has changed origin
			activeClipmap.originDelta = activeClipmap.origin - activeClipmap.previousOrigin;
			Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", activeClipmap.originDelta / (voxelSpaceSize * activeClipmap.localScale));






			//Calculate the relative origin and overlap/size of the previous cascade as compared to the active cascade. This is used to avoid voxelizing areas that have already been voxelized by previous (smaller) cascades
			Vector3 prevClipmapRelativeOrigin = Vector3.zero;
			float prevClipmapOccupance = 0.0f;
			if (currentClipmapIndex != 0)
			{
				prevClipmapRelativeOrigin = (prevClipmap.origin - activeClipmap.origin) / clipmapSize;
				prevClipmapOccupance = prevClipmap.localScale / activeClipmap.localScale;
			}
			Shader.SetGlobalVector("SEGIClipmapOverlap", new Vector4(prevClipmapRelativeOrigin.x, prevClipmapRelativeOrigin.y, prevClipmapRelativeOrigin.z, prevClipmapOccupance));

			//Calculate the relative origin and scale of this cascade as compared to the first (level 0) cascade. This is used during GI tracing/data lookup to ensure tracing is done in the correct space
			for (int i = 1; i < numClipmaps; i++)
			{
				Vector3 clipPosFromMaster = Vector3.zero;
				float clipScaleFromMaster = 1.0f;

				clipPosFromMaster = (clipmaps[i].origin - clipmaps[0].origin) / (voxelSpaceSize * clipmaps[i].localScale);
				clipScaleFromMaster = clipmaps[0].localScale / clipmaps[i].localScale;

				Shader.SetGlobalVector("SEGIClipTransform" + i.ToString(), new Vector4(clipPosFromMaster.x, clipPosFromMaster.y, clipPosFromMaster.z, clipScaleFromMaster));
			}

			//Set the voxel camera (proxy camera used to render the scene for voxelization) parameters
			voxelCamera.enabled = false;
			voxelCamera.orthographic = true;
			voxelCamera.orthographicSize = clipmapSize * 0.5f;
			voxelCamera.nearClipPlane = 0.0f;
			voxelCamera.farClipPlane = clipmapSize;
			voxelCamera.depth = -2;
			voxelCamera.renderingPath = RenderingPath.Forward;
			voxelCamera.clearFlags = CameraClearFlags.Color;
			voxelCamera.backgroundColor = Color.black;
			voxelCamera.cullingMask = giCullingMask;

			//Move the voxel camera game object and other related objects to the above calculated voxel space origin
			voxelCameraGO.transform.position = activeClipmap.origin - Vector3.forward * clipmapSize * 0.5f;
			voxelCameraGO.transform.rotation = rotationFront;

			leftViewPoint.transform.position = activeClipmap.origin + Vector3.left * clipmapSize * 0.5f;
			leftViewPoint.transform.rotation = rotationLeft;
			topViewPoint.transform.position = activeClipmap.origin + Vector3.up * clipmapSize * 0.5f;
			topViewPoint.transform.rotation = rotationTop;




			//Set matrices needed for voxelization
			//Shader.SetGlobalMatrix("WorldToGI", shadowCam.worldToCameraMatrix);
			//Shader.SetGlobalMatrix("GIToWorld", shadowCam.cameraToWorldMatrix);
			//Shader.SetGlobalMatrix("GIProjection", shadowCam.projectionMatrix);
			//Shader.SetGlobalMatrix("GIProjectionInverse", shadowCam.projectionMatrix.inverse);
			Shader.SetGlobalMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
			Shader.SetGlobalFloat("GIDepthRatio", shadowSpaceDepthRatio);
			
			Matrix4x4 frontViewMatrix = TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix);
			Matrix4x4 leftViewMatrix = TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix);
			Matrix4x4 topViewMatrix = TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix);

			Shader.SetGlobalMatrix("SEGIVoxelViewFront", frontViewMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelViewLeft", leftViewMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelViewTop", topViewMatrix);
			Shader.SetGlobalMatrix("SEGIWorldToVoxel", voxelCamera.worldToCameraMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelProjection", voxelCamera.projectionMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelProjectionInverse", voxelCamera.projectionMatrix.inverse);

			Shader.SetGlobalMatrix("SEGIVoxelVPFront", GL.GetGPUProjectionMatrix(voxelCamera.projectionMatrix, true) * frontViewMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelVPLeft", GL.GetGPUProjectionMatrix(voxelCamera.projectionMatrix, true) * leftViewMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelVPTop", GL.GetGPUProjectionMatrix(voxelCamera.projectionMatrix, true) * topViewMatrix);

			Shader.SetGlobalMatrix("SEGIWorldToVoxel" + currentClipmapIndex.ToString(), voxelCamera.worldToCameraMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelProjection" + currentClipmapIndex.ToString(), voxelCamera.projectionMatrix);

			Matrix4x4 voxelToGIProjection = shadowCam.projectionMatrix * shadowCam.worldToCameraMatrix * voxelCamera.cameraToWorldMatrix;
			Shader.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);
			Shader.SetGlobalVector("SEGISunlightVector", sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);


			//Set paramteters
			Shader.SetGlobalInt("SEGIVoxelResolution", (int)voxelResolution);
			
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

				Vector3 shadowCamPosition = activeClipmap.origin + Vector3.Normalize(-sun.transform.forward) * clipmapShadowSize * 0.5f * shadowSpaceDepthRatio;

				shadowCamTransform.position = shadowCamPosition;
				shadowCamTransform.LookAt(activeClipmap.origin, Vector3.up);

				shadowCam.renderingPath = RenderingPath.Forward;
				shadowCam.depthTextureMode |= DepthTextureMode.None;

				shadowCam.orthographicSize = clipmapShadowSize;
				shadowCam.farClipPlane = clipmapShadowSize * 2.0f * shadowSpaceDepthRatio;

                //Shader.SetGlobalMatrix("WorldToGI", shadowCam.worldToCameraMatrix);
                //Shader.SetGlobalMatrix("GIToWorld", shadowCam.cameraToWorldMatrix);
                //Shader.SetGlobalMatrix("GIProjection", shadowCam.projectionMatrix);
                //Shader.SetGlobalMatrix("GIProjectionInverse", shadowCam.projectionMatrix.inverse);
                voxelToGIProjection = shadowCam.projectionMatrix * shadowCam.worldToCameraMatrix * voxelCamera.cameraToWorldMatrix;
                Shader.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);


				Graphics.SetRenderTarget(sunDepthTexture);
				shadowCam.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);

				shadowCam.RenderWithShader(sunDepthShader, "");

				Shader.SetGlobalTexture("SEGISunDepth", sunDepthTexture);
			}







			//Clear the volume texture that is immediately written to in the voxelization scene shader
			clearCompute.SetTexture(0, "RG0", integerVolume);
			clearCompute.SetInt("Res", activeClipmap.resolution);
			clearCompute.Dispatch(0, activeClipmap.resolution / 16, activeClipmap.resolution / 16, 1);




			//Set irradiance "secondary bounce" texture
			Shader.SetGlobalTexture("SEGICurrentIrradianceVolume", irradianceClipmaps[currentClipmapIndex].volumeTexture0);

			
			Graphics.SetRandomWriteTarget(1, integerVolume);
			voxelCamera.targetTexture = dummyVoxelTextureAAScaled;
			voxelCamera.RenderWithShader(voxelizationShader, "");
			Graphics.ClearRandomWriteTargets();


			//Transfer the data from the volume integer texture to the main volume texture used for GI tracing. 
			transferIntsCompute.SetTexture(0, "Result", activeClipmap.volumeTexture0);
			transferIntsCompute.SetTexture(0, "RG0", integerVolume);
			transferIntsCompute.SetInt("VoxelAA", voxelAA ? 3 : 0);
			transferIntsCompute.SetInt("Resolution", activeClipmap.resolution);
			transferIntsCompute.Dispatch(0, activeClipmap.resolution / 16, activeClipmap.resolution / 16, 1);



			//Push current voxelization result to higher levels
			for (int i = 0 + 1; i < numClipmaps; i++)
			{
				Clipmap sourceClipmap = clipmaps[i - 1];
				Clipmap targetClipmap = clipmaps[i];


				Vector3 sourceRelativeOrigin = Vector3.zero;
				float sourceOccupance = 0.0f;

				sourceRelativeOrigin = (sourceClipmap.origin - targetClipmap.origin) / (targetClipmap.localScale * voxelSpaceSize);
				sourceOccupance = sourceClipmap.localScale / targetClipmap.localScale;

				mipFilterCompute.SetTexture(0, "Source", sourceClipmap.volumeTexture0);
				mipFilterCompute.SetTexture(0, "Destination", targetClipmap.volumeTexture0);
				mipFilterCompute.SetVector("ClipmapOverlap", new Vector4(sourceRelativeOrigin.x, sourceRelativeOrigin.y, sourceRelativeOrigin.z, sourceOccupance));
				mipFilterCompute.SetInt("destinationRes", targetClipmap.resolution);
				mipFilterCompute.Dispatch(0, targetClipmap.resolution / 16, targetClipmap.resolution / 16, 1);
			}



			for (int i = 0; i < numClipmaps; i++)
			{
				Shader.SetGlobalTexture("SEGIVolumeLevel" + i.ToString(), clipmaps[i].volumeTexture0);
			}


			if (infiniteBounces)
			{
				renderState = RenderState.Bounce;
			}
			else
			{
				//Increment clipmap counter
				clipmapCounter++;
				if (clipmapCounter >= (int)Mathf.Pow(2.0f, numClipmaps))
				{
					clipmapCounter = 0;
				}
			}
		}
		else if (renderState == RenderState.Bounce)
		{
			//Calculate the relative position and scale of the current clipmap as compared to the first (level 0) clipmap. Used to ensure tracing is performed in the correct space
			Vector3 translateToZero = Vector3.zero;
			translateToZero = (clipmaps[currentClipmapIndex].origin - clipmaps[0].origin) / (voxelSpaceSize * clipmaps[currentClipmapIndex].localScale);
			float scaleToZero = 1.0f / clipmaps[currentClipmapIndex].localScale;
			Shader.SetGlobalVector("SEGICurrentClipTransform", new Vector4(translateToZero.x, translateToZero.y, translateToZero.z, scaleToZero));

			//Clear the volume texture that is immediately written to in the voxelization scene shader
			clearCompute.SetTexture(0, "RG0", integerVolume);
			clearCompute.SetInt("Res", clipmaps[currentClipmapIndex].resolution);
			clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			//Only render infinite bounces for clipmaps 0, 1, and 2
			if (currentClipmapIndex <= 2)
			{
				Shader.SetGlobalInt("SEGISecondaryCones", secondaryCones);
				Shader.SetGlobalFloat("SEGISecondaryOcclusionStrength", secondaryOcclusionStrength);

				Graphics.SetRandomWriteTarget(1, integerVolume);
				voxelCamera.targetTexture = dummyVoxelTextureFixed;
				voxelCamera.RenderWithShader(voxelTracingShader, "");
				Graphics.ClearRandomWriteTargets();

				transferIntsCompute.SetTexture(1, "Result", irradianceClipmaps[currentClipmapIndex].volumeTexture0);
				transferIntsCompute.SetTexture(1, "RG0", integerVolume);
				transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
				transferIntsCompute.Dispatch(1, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);
			}

			//Increment clipmap counter
			clipmapCounter++;
			if (clipmapCounter >= (int)Mathf.Pow(2.0f, numClipmaps))
			{
				clipmapCounter = 0;
			}

			renderState = RenderState.Voxelize;

		}
		Matrix4x4 giToVoxelProjection = voxelCamera.projectionMatrix * voxelCamera.worldToCameraMatrix * shadowCam.cameraToWorldMatrix;
		Shader.SetGlobalMatrix("GIToVoxelProjection", giToVoxelProjection);



		RenderTexture.active = previousActive;

		//Set the sun's shadow setting back to what it was before voxelization
		if (sun != null)
		{
			sun.shadows = prevSunShadowSetting;
		}
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
		material.SetTexture("NoiseTexture", blueNoise[frameCounter]);
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

		//Get the camera depth and normals
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
		material.SetTexture("PreviousDepth", previousDepth);

		//Render diffuse GI tracing result
		Graphics.Blit(source, gi2, material, Pass.DiffuseTrace);
		if (doReflections)
		{			
			//Render GI reflections result
			Graphics.Blit(source, reflections, material, Pass.SpecularTrace);
			material.SetTexture("Reflections", reflections);
		}


		//Perform bilateral filtering
		if (useBilateralFiltering && temporalBlendWeight >= 0.99999f)
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



			//Perform a bilateral blur to be applied in newly revealed areas that are still noisy due to not having previous data blended with it
			RenderTexture blur0 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
			RenderTexture blur1 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
			material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
			Graphics.Blit(gi3, blur1, material, Pass.BilateralBlur);

			material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
			Graphics.Blit(blur1, blur0, material, Pass.BilateralBlur);

			material.SetVector("Kernel", new Vector2(0.0f, 2.0f));
			Graphics.Blit(blur0, blur1, material, Pass.BilateralBlur);

			material.SetVector("Kernel", new Vector2(2.0f, 0.0f));
			Graphics.Blit(blur1, blur0, material, Pass.BilateralBlur);

			material.SetTexture("BlurredGI", blur0);


			//Perform temporal reprojection and blending
			if (temporalBlendWeight < 1.0f)
			{
				Graphics.Blit(gi3, gi4);
				Graphics.Blit(gi4, gi3, material, Pass.TemporalBlend);
				Graphics.Blit(gi3, previousGIResult);
				Graphics.Blit(source, previousDepth, material, Pass.GetCameraDepthTexture);

							
				//Perform bilateral filtering on temporally blended result
				if (useBilateralFiltering)
				{
					material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
					Graphics.Blit(gi3, gi4, material, Pass.BilateralBlur);

					material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
					Graphics.Blit(gi4, gi3, material, Pass.BilateralBlur);

					material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
					Graphics.Blit(gi3, gi4, material, Pass.BilateralBlur);

					material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
					Graphics.Blit(gi4, gi3, material, Pass.BilateralBlur);
				}
			}





			//Set the result to be accessed in the shader
			material.SetTexture("GITexture", gi3);

			//Actually apply the GI to the scene using gbuffer data
			Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);

			//Release temporary textures
			RenderTexture.ReleaseTemporary(blur0);
            RenderTexture.ReleaseTemporary(blur1);
			RenderTexture.ReleaseTemporary(gi3);
			RenderTexture.ReleaseTemporary(gi4);
		}
		else	//If Half Resolution tracing is disabled
		{
			if (temporalBlendWeight < 1.0f)
			{
				//Perform a bilateral blur to be applied in newly revealed areas that are still noisy due to not having previous data blended with it
                RenderTexture blur0 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
                RenderTexture blur1 = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
                material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
                Graphics.Blit(gi2, blur1, material, Pass.BilateralBlur);

                material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
                Graphics.Blit(blur1, blur0, material, Pass.BilateralBlur);

                material.SetVector("Kernel", new Vector2(0.0f, 2.0f));
                Graphics.Blit(blur0, blur1, material, Pass.BilateralBlur);

                material.SetVector("Kernel", new Vector2(2.0f, 0.0f));
                Graphics.Blit(blur1, blur0, material, Pass.BilateralBlur);

                material.SetTexture("BlurredGI", blur0);




				//Perform temporal reprojection and blending
                Graphics.Blit(gi2, gi1, material, Pass.TemporalBlend);
				Graphics.Blit(gi1, previousGIResult);
				Graphics.Blit(source, previousDepth, material, Pass.GetCameraDepthTexture);



				//Perform bilateral filtering on temporally blended result
				if (useBilateralFiltering)
				{
					material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
					Graphics.Blit(gi1, gi2, material, Pass.BilateralBlur);

					material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
					Graphics.Blit(gi2, gi1, material, Pass.BilateralBlur);

					material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
					Graphics.Blit(gi1, gi2, material, Pass.BilateralBlur);

					material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
					Graphics.Blit(gi2, gi1, material, Pass.BilateralBlur);
				}



                RenderTexture.ReleaseTemporary(blur0);
                RenderTexture.ReleaseTemporary(blur1);
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
