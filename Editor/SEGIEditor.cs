using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

[CustomEditor(typeof(SEGI))]
public class SEGIEditor : Editor
{
	SerializedObject serObj;

	SerializedProperty voxelResolution;
	SerializedProperty visualizeSunDepthTexture;
	SerializedProperty visualizeGI;
	SerializedProperty sun;
	SerializedProperty giCullingMask;
	SerializedProperty shadowSpaceSize;
	SerializedProperty temporalBlendWeight;
	SerializedProperty visualizeVoxels;
	SerializedProperty updateGI;
	SerializedProperty skyColor;
	SerializedProperty voxelSpaceSize;
	SerializedProperty useBilateralFiltering;
	SerializedProperty halfResolution;
	SerializedProperty stochasticSampling;
	SerializedProperty infiniteBounces;
	SerializedProperty followTransform;
	SerializedProperty cones;
	SerializedProperty coneTraceSteps;
	SerializedProperty coneLength;
	SerializedProperty coneWidth;
	SerializedProperty occlusionStrength;
	SerializedProperty nearOcclusionStrength;
	SerializedProperty occlusionPower;
	SerializedProperty coneTraceBias;
	SerializedProperty nearLightGain;
	SerializedProperty giGain;
	SerializedProperty secondaryBounceGain;
	SerializedProperty softSunlight;
	SerializedProperty doReflections;
	SerializedProperty voxelAA;
	SerializedProperty reflectionSteps;
	SerializedProperty skyReflectionIntensity;
	SerializedProperty gaussianMipFilter;
	SerializedProperty reflectionOcclusionPower;
	SerializedProperty farOcclusionStrength;
	SerializedProperty farthestOcclusionStrength;
	SerializedProperty secondaryCones;
	SerializedProperty secondaryOcclusionStrength;
	SerializedProperty skyIntensity;
	SerializedProperty sphericalSkylight;
	SerializedProperty innerOcclusionLayers;

	SEGI instance;

	const string presetPath = "Assets/SEGI/Resources/Presets";

	GUIStyle headerStyle;
	GUIStyle vramLabelStyle
	{
		get
		{
			GUIStyle s = new GUIStyle(EditorStyles.boldLabel);
			s.fontStyle = FontStyle.Italic;
			return s;
		}
	}


	bool showMainConfig = true;
	bool showDebugTools = false;
	bool showTracingProperties = true;
	bool showEnvironmentProperties = true;
	bool showPresets = true;
	bool showReflectionProperties = true;

	string presetToSaveName;

	int presetPopupIndex;

	void OnEnable()
	{
		serObj = new SerializedObject(target);

		voxelResolution = serObj.FindProperty("voxelResolution");
		visualizeSunDepthTexture = serObj.FindProperty("visualizeSunDepthTexture");
		visualizeGI = serObj.FindProperty("visualizeGI");
		sun = serObj.FindProperty("sun");
		giCullingMask = serObj.FindProperty("giCullingMask");
		shadowSpaceSize = serObj.FindProperty("shadowSpaceSize");
		temporalBlendWeight = serObj.FindProperty("temporalBlendWeight");
		visualizeVoxels = serObj.FindProperty("visualizeVoxels");
		updateGI = serObj.FindProperty("updateGI");
		skyColor = serObj.FindProperty("skyColor");
		voxelSpaceSize = serObj.FindProperty("voxelSpaceSize");
		useBilateralFiltering = serObj.FindProperty("useBilateralFiltering");
		halfResolution = serObj.FindProperty("halfResolution");
		stochasticSampling = serObj.FindProperty("stochasticSampling");
		infiniteBounces = serObj.FindProperty("infiniteBounces");
		followTransform = serObj.FindProperty("followTransform");
		cones = serObj.FindProperty("cones");
		coneTraceSteps = serObj.FindProperty("coneTraceSteps");
		coneLength = serObj.FindProperty("coneLength");
		coneWidth = serObj.FindProperty("coneWidth");
		occlusionStrength = serObj.FindProperty("occlusionStrength");
		nearOcclusionStrength = serObj.FindProperty("nearOcclusionStrength");
		occlusionPower = serObj.FindProperty("occlusionPower");
		coneTraceBias = serObj.FindProperty("coneTraceBias");
		nearLightGain = serObj.FindProperty("nearLightGain");
		giGain = serObj.FindProperty("giGain");
		secondaryBounceGain = serObj.FindProperty("secondaryBounceGain");
		softSunlight = serObj.FindProperty("softSunlight");
		doReflections = serObj.FindProperty("doReflections");
		voxelAA = serObj.FindProperty("voxelAA");
		reflectionSteps = serObj.FindProperty("reflectionSteps");
		skyReflectionIntensity = serObj.FindProperty("skyReflectionIntensity");
		gaussianMipFilter = serObj.FindProperty("gaussianMipFilter");
		reflectionOcclusionPower = serObj.FindProperty("reflectionOcclusionPower");
		farOcclusionStrength = serObj.FindProperty("farOcclusionStrength");
		farthestOcclusionStrength = serObj.FindProperty("farthestOcclusionStrength");
		secondaryCones = serObj.FindProperty("secondaryCones");
		secondaryOcclusionStrength = serObj.FindProperty("secondaryOcclusionStrength");
		skyIntensity = serObj.FindProperty("skyIntensity");
		sphericalSkylight = serObj.FindProperty("sphericalSkylight");
		innerOcclusionLayers = serObj.FindProperty("innerOcclusionLayers");


		instance = target as SEGI;
	}

	public override void OnInspectorGUI()
	{
		serObj.Update();

		//Presets
		showPresets = EditorGUILayout.Foldout(showPresets, new GUIContent("Presets"));
		if (showPresets)
		{
			EditorGUI.indentLevel++;
			string[] presetGUIDs = AssetDatabase.FindAssets("t:SEGIPreset", new string[1] { presetPath });
			string[] presetNames = new string[presetGUIDs.Length];
			string[] presetPaths = new string[presetGUIDs.Length];

			for (int i = 0; i < presetGUIDs.Length; i++)
			{
				presetPaths[i] = AssetDatabase.GUIDToAssetPath(presetGUIDs[i]);
				presetNames[i] = System.IO.Path.GetFileNameWithoutExtension(presetPaths[i]);
			}

			EditorGUILayout.BeginHorizontal();
			presetPopupIndex = EditorGUILayout.Popup("", presetPopupIndex, presetNames);

			if (GUILayout.Button("Load"))
			{
				if (presetPaths.Length > 0)
				{
					SEGIPreset preset = AssetDatabase.LoadAssetAtPath<SEGIPreset>(presetPaths[presetPopupIndex]);
					Undo.RecordObject(target, "Loaded SEGI Preset");
					instance.ApplyPreset(preset);
				}
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			presetToSaveName = EditorGUILayout.TextField(presetToSaveName);

			if (GUILayout.Button("Save"))
			{
				SavePreset(presetToSaveName);
			}
			EditorGUILayout.EndHorizontal();
			EditorGUI.indentLevel--;
		}

		EditorGUILayout.Space();
		EditorGUILayout.Space();

		//Main Configuration
		showMainConfig = EditorGUILayout.Foldout(showMainConfig, new GUIContent("Main Configuration"));
		if (showMainConfig)
		{
			EditorGUI.indentLevel++;
			EditorGUILayout.BeginVertical();
			EditorGUILayout.PropertyField(voxelResolution, new GUIContent("Voxel Resolution", "The resolution of the voxel texture used to calculate GI."));
			EditorGUILayout.PropertyField(voxelAA, new GUIContent("Voxel AA", "Enables anti-aliasing during voxelization for higher precision voxels."));
			EditorGUILayout.PropertyField(innerOcclusionLayers, new GUIContent("Inner Occlusion Layers", "Enables the writing of additional black occlusion voxel layers on the back face of geometry. Can help with light leaking but may cause artifacts with small objects."));
			EditorGUILayout.PropertyField(gaussianMipFilter, new GUIContent("Gaussian Mip Filter", "Enables gaussian filtering during mipmap generation. This can improve visual smoothness and consistency, particularly with large moving objects."));
			EditorGUILayout.PropertyField(voxelSpaceSize, new GUIContent("Voxel Space Size", "The size of the voxel volume in world units. Everything inside the voxel volume will contribute to GI."));
			EditorGUILayout.PropertyField(shadowSpaceSize, new GUIContent("Shadow Space Size", "The size of the sun shadow texture used to inject sunlight with shadows into the voxels in world units. It is recommended to set this value similar to Voxel Space Size."));
			EditorGUILayout.PropertyField(giCullingMask, new GUIContent("GI Culling Mask", "Which layers should be voxelized and contribute to GI."));
			EditorGUILayout.PropertyField(updateGI, new GUIContent("Update GI", "Whether voxelization and multi-bounce rendering should update every frame. When disabled, GI tracing will use cached data from the last time this was enabled."));
			EditorGUILayout.PropertyField(infiniteBounces, new GUIContent("Infinite Bounces", "Enables infinite bounces. This is expensive for complex scenes and is still experimental."));
			EditorGUILayout.PropertyField(followTransform, new GUIContent("Follow Transform", "If provided, the voxel volume will follow and be centered on this object instead of the camera. Useful for top-down scenes."));
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("VRAM Usage: " + instance.vramUsage.ToString("F2") + " MB", vramLabelStyle);
			EditorGUI.indentLevel--;
		}

		EditorGUILayout.Space();
		EditorGUILayout.Space();




		//Environment
		showEnvironmentProperties = EditorGUILayout.Foldout(showEnvironmentProperties, new GUIContent("Environment Properties"));
		if (instance.sun == null)
		{
			showEnvironmentProperties = true;
		}
		if (showEnvironmentProperties)
		{
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(sun, new GUIContent("Sun", "The main directional light that will cast indirect light into the scene (sunlight or moonlight)."));
			EditorGUILayout.PropertyField(softSunlight, new GUIContent("Soft Sunlight", "The amount of soft diffuse sunlight that will be added to the scene. Use this to simulate the effect of clouds/haze scattering soft sunlight onto the scene."));
			EditorGUILayout.PropertyField(skyColor, new GUIContent("Sky Color", "The color of the light scattered onto the scene coming from the sky."));
			EditorGUILayout.PropertyField(skyIntensity, new GUIContent("Sky Intensity", "The brightness of the sky light."));
			EditorGUILayout.PropertyField(sphericalSkylight, new GUIContent("Spherical Skylight", "If enabled, light from the sky will come from all directions. If disabled, light from the sky will only come from the top hemisphere."));
			EditorGUI.indentLevel--;
		}

		EditorGUILayout.Space();
		EditorGUILayout.Space();


		//Tracing properties
		showTracingProperties = EditorGUILayout.Foldout(showTracingProperties, new GUIContent("Tracing Properties"));
		if (showTracingProperties)
		{
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(temporalBlendWeight, new GUIContent("Temporal Blend Weight", "The lower the value, the more previous frames will be blended with the current frame. Lower values result in smoother GI that updates less quickly."));
			EditorGUILayout.PropertyField(useBilateralFiltering, new GUIContent("Bilateral Filtering", "Enables filtering of the GI result to reduce noise."));
			EditorGUILayout.PropertyField(halfResolution, new GUIContent("Half Resolution", "If enabled, GI tracing will be done at half screen resolution. Improves speed of GI tracing."));
			EditorGUILayout.PropertyField(stochasticSampling, new GUIContent("Stochastic Sampling", "If enabled, uses random jitter to reduce banding and discontinuities during GI tracing."));

			EditorGUILayout.PropertyField(cones, new GUIContent("Cones", "The number of cones that will be traced in different directions for diffuse GI tracing. More cones result in a smoother result at the cost of performance."));
			EditorGUILayout.PropertyField(coneTraceSteps, new GUIContent("Cone Trace Steps", "The number of tracing steps for each cone. Too few results in skipping thin features. Higher values result in more accuracy at the cost of performance."));
			EditorGUILayout.PropertyField(coneLength, new GUIContent("Cone length", "The number of cones that will be traced in different directions for diffuse GI tracing. More cones result in a smoother result at the cost of performance."));
			EditorGUILayout.PropertyField(coneWidth, new GUIContent("Cone Width", "The width of each cone. Wider cones cause a softer and smoother result but affect accuracy and incrase over-occlusion. Thinner cones result in more accurate tracing with less coherent (more noisy) results and a higher tracing cost."));
			EditorGUILayout.PropertyField(coneTraceBias, new GUIContent("Cone Trace Bias", "The amount of offset above a surface that cone tracing begins. Higher values reduce \"voxel acne\" (similar to \"shadow acne\"). Values that are too high result in light-leaking."));
			EditorGUILayout.PropertyField(occlusionStrength, new GUIContent("Occlusion Strength", "The strength of shadowing solid objects will cause. Affects the strength of all indirect shadows."));
			EditorGUILayout.PropertyField(nearOcclusionStrength, new GUIContent("Near Occlusion Strength", "The strength of shadowing nearby solid objects will cause. Only affects the strength of very close blockers."));
			EditorGUILayout.PropertyField(farOcclusionStrength, new GUIContent("Far Occlusion Strength", "How much light far occluders block. This value gives additional light blocking proportional to the width of the cone at each trace step."));
			EditorGUILayout.PropertyField(farthestOcclusionStrength, new GUIContent("Farthest Occlusion Strength", "How much light the farthest occluders block. This value gives additional light blocking proportional to (cone width)^2 at each trace step."));
			EditorGUILayout.PropertyField(occlusionPower, new GUIContent("Occlusion Power", "The strength of shadowing far solid objects will cause. Only affects the strength of far blockers. Decrease this value if wide cones are causing over-occlusion."));
			EditorGUILayout.PropertyField(nearLightGain, new GUIContent("Near Light Gain", "Affects the attenuation of indirect light. Higher values allow for more close-proximity indirect light. Lower values reduce close-proximity indirect light, sometimes resulting in a cleaner result."));
			EditorGUILayout.PropertyField(giGain, new GUIContent("GI Gain", "The overall brightness of indirect light. For Near Light Gain values around 1, a value of 1 for this property is recommended for a physically-accurate result."));
			EditorGUILayout.Space();
			EditorGUILayout.PropertyField(secondaryBounceGain, new GUIContent("Secondary Bounce Gain", "Affects the strength of secondary/infinite bounces. Be careful, values above 1 can cause runaway light bouncing and flood areas with extremely bright light!"));
			EditorGUILayout.PropertyField(secondaryCones, new GUIContent("Secondary Cones", "The number of secondary cones that will be traced for calculating infinte bounces. Increasing this value improves the accuracy of secondary bounces at the cost of performance. Note: the performance cost of this scales with voxelized scene complexity."));
			EditorGUILayout.PropertyField(secondaryOcclusionStrength, new GUIContent("Secondary Occlusion Strength", "The strength of light blocking during secondary bounce tracing. Be careful, a value too low can cause runaway light bouncing and flood areas with extremely bright light!"));
			EditorGUI.indentLevel--;
		}

		EditorGUILayout.Space();
		EditorGUILayout.Space();

		showReflectionProperties = EditorGUILayout.Foldout(showReflectionProperties, new GUIContent("Reflection Properties"));
		if (showReflectionProperties)
		{
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(doReflections, new GUIContent("Do Reflections", "Enable this for cone-traced reflections."));
			EditorGUILayout.PropertyField(reflectionSteps, new GUIContent("Reflection Steps", "Number of reflection trace steps."));
			EditorGUILayout.PropertyField(reflectionOcclusionPower, new GUIContent("Reflection Occlusion Power", "Strength of light blocking during reflection tracing."));
			EditorGUILayout.PropertyField(skyReflectionIntensity, new GUIContent("Sky Reflection Intensity", "Intensity of sky reflections."));
			EditorGUI.indentLevel--;
		}

		EditorGUILayout.Space();
		EditorGUILayout.Space();

		//Debug tools
		showDebugTools = EditorGUILayout.Foldout(showDebugTools, new GUIContent("Debug Tools"));
		if (showDebugTools)
		{
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(visualizeSunDepthTexture, new GUIContent("Visualize Sun Depth Texture", "Visualize the depth texture used to render proper shadows while injecting sunlight into voxel data."));
			EditorGUILayout.PropertyField(visualizeGI, new GUIContent("Visualize GI", "Visualize GI result only (no textures)."));
			EditorGUILayout.PropertyField(visualizeVoxels, new GUIContent("Visualize Voxels", "Directly view the voxels in the scene."));
			EditorGUI.indentLevel--;
		}


		serObj.ApplyModifiedProperties();
	}

	void SavePreset(string name)
	{
		if (name == "")
		{
			Debug.LogWarning("SEGI: Type in a name for the preset to be saved!");
			return; 
		}

		//SEGIPreset preset = new SEGIPreset();
		SEGIPreset preset = ScriptableObject.CreateInstance<SEGIPreset>();

		preset.voxelResolution = instance.voxelResolution;
		preset.voxelAA = instance.voxelAA;
		preset.innerOcclusionLayers = instance.innerOcclusionLayers;
		preset.infiniteBounces = instance.infiniteBounces;

		preset.temporalBlendWeight = instance.temporalBlendWeight;
		preset.useBilateralFiltering = instance.useBilateralFiltering;
		preset.halfResolution = instance.halfResolution;
		preset.stochasticSampling = instance.stochasticSampling;
		preset.doReflections = instance.doReflections;

		preset.cones = instance.cones;
		preset.coneTraceSteps = instance.coneTraceSteps;
		preset.coneLength = instance.coneLength;
		preset.coneWidth = instance.coneWidth;
		preset.coneTraceBias = instance.coneTraceBias;
		preset.occlusionStrength = instance.occlusionStrength;
		preset.nearOcclusionStrength = instance.nearOcclusionStrength;
		preset.occlusionPower = instance.occlusionPower;
		preset.nearLightGain = instance.nearLightGain;
		preset.giGain = instance.giGain;
		preset.secondaryBounceGain = instance.secondaryBounceGain;

		preset.reflectionSteps = instance.reflectionSteps;
		preset.reflectionOcclusionPower = instance.reflectionOcclusionPower;
		preset.skyReflectionIntensity = instance.skyReflectionIntensity;
		preset.gaussianMipFilter = instance.gaussianMipFilter;

		preset.farOcclusionStrength = instance.farOcclusionStrength;
		preset.farthestOcclusionStrength = instance.farthestOcclusionStrength;
		preset.secondaryCones = instance.secondaryCones;
		preset.secondaryOcclusionStrength = instance.secondaryOcclusionStrength;

		string path = presetPath + "/";

		AssetDatabase.CreateAsset(preset, path + name + ".asset");

		AssetDatabase.SaveAssets();
	}

	void LoadPreset()
	{

	}
}
