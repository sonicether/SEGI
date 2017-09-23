using UnityEngine;
using System.Collections;

public class FirstPersonFlyingController : MonoBehaviour 
{
	public float speed = 1.0f;

	public Transform cameraTransform;


	Transform t;

	Vector3 movementVectorSmooth;
	Vector3 rotationVectorSmooth;

	public GameObject checkSphere;

	public bool dontRequireClick = false;

	public bool limitPosition = false;

	public Vector3 minPosition;
	public Vector3 maxPosition;

	// Use this for initialization
	void Start () 
	{
		t = GetComponent<Transform>();
	}
	
	void LateUpdate () 
	{
//		if (DemoAnimation.instance.demoMode == DemoAnimation.DemoMode.Interactive)
//		{
			Movement();
			MouseLook();

		//if (Input.GetKey(KeyCode.F))
		//{
			//checkSphere.transform.position = cameraTransform.position + cameraTransform.forward * 2.0f;
		//}
//		}
	}

	void Movement()
	{
		Vector3 movementVector = Vector3.zero;

		if (Input.GetKey(KeyCode.W))
			movementVector.z += 1.0f;

		if (Input.GetKey(KeyCode.S))
			movementVector.z -= 1.0f;

		if (Input.GetKey(KeyCode.A))
			movementVector.x -= 1.0f;

		if (Input.GetKey(KeyCode.D))
			movementVector.x += 1.0f;

		if (Input.GetKey(KeyCode.Space))
			movementVector.y += 1.0f;

		if (Input.GetKey(KeyCode.LeftShift))
			movementVector.y -= 1.0f;

		movementVector = Vector3.Normalize(movementVector);

//		movementVector *= Input.GetKey(KeyCode.LeftAlt) ? 0.2f : 1.0f;

		movementVectorSmooth = Vector3.Lerp(movementVectorSmooth, movementVector, 5.0f * Time.deltaTime);

		t.Translate(movementVectorSmooth * 9.0f * Time.deltaTime * speed);

		if (limitPosition)
		{
			Vector3 pos = t.position;
			pos.x = Mathf.Clamp(pos.x, minPosition.x, maxPosition.x);
			pos.y = Mathf.Clamp(pos.y, minPosition.y, maxPosition.y);
			pos.z = Mathf.Clamp(pos.z, minPosition.z, maxPosition.z);
			t.position = pos;
		}
	}

	void MouseLook()
	{
		Vector3 rotationVector = Vector3.zero;

		if (Input.GetKey(KeyCode.Mouse0) || dontRequireClick)
		{
			rotationVector.y += (Input.GetAxis("Mouse X")) * 1.00f;
			rotationVector.x -= (Input.GetAxis("Mouse Y")) * 1.00f;
		}

		rotationVectorSmooth = Vector3.Lerp(rotationVectorSmooth, rotationVector, 5.0f * Time.deltaTime);

		t.Rotate(new Vector3(0.0f, rotationVectorSmooth.y * 2.0f, 0.0f));
		cameraTransform.Rotate(Vector3.right * rotationVectorSmooth.x * 2.0f);
	}
}
