using UnityEngine;
using System.Collections;

public class SEGIThrowObject : MonoBehaviour
{

	Color generatedColor;
	Renderer r;

	float pulseSpeed;
	float pulseOffset;

	public Material mat0;
	public Material mat1;
	public Material mat2;
	public Material mat3;
	public Material mat4;

	void Awake()
	{
		r = GetComponent<Renderer>();

		int matIndex = Random.Range(0, 5);

		switch(matIndex)
		{
			case 0:
				r.material = mat0;
				break;
			case 1:
				r.material = mat1;
				break;
			case 2:
				r.material = mat2;
				break;
			case 3:
				r.material = mat3;
				break;
			case 4:
				r.material = mat4;
				break;
			default:
				break;
		}

	}
}
