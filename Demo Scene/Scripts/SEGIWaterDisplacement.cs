using UnityEngine;
using System.Collections;

[RequireComponent(typeof(MeshFilter))]
public class SEGIWaterDisplacement : MonoBehaviour 
{
	MeshFilter meshFilter;
	Mesh mesh;
	Vector3[] initialVertices;

	// Use this for initialization
	void Start () 
	{
		meshFilter = GetComponent<MeshFilter>(); 
		mesh = meshFilter.mesh;
		initialVertices = mesh.vertices;
	}
	
	// Update is called once per frame
	void Update () 
	{
		Vector3[] vertices = mesh.vertices;
		float scale = 1.0f;
		float amp = 0.1f;
		for (int i = 0; i < vertices.Length; i++)
		{
			vertices[i].y = 0.0f
			+ Mathf.Sin(Time.time + initialVertices[i].x * scale * 1.0f) * 0.25f * amp
			+ Mathf.Sin(Time.time + initialVertices[i].x * scale * 0.15278f) * 0.25f * amp
			+ Mathf.Sin(Time.time * 1.5f + initialVertices[i].x * scale * 1.15278f + initialVertices[i].z * scale * 0.4f) * 0.25f * amp
			+ Mathf.PerlinNoise(initialVertices[i].x * 0.85f + Time.time, initialVertices[i].z * 0.85f) * amp
			+ Mathf.PerlinNoise(initialVertices[i].x * 2.85f + Time.time, initialVertices[i].z * 4.85f) * 0.5f * amp;
		}
		mesh.vertices = vertices;
		mesh.RecalculateNormals();

	}
}
