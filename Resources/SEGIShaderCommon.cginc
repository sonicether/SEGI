sampler2D _MainTex;
float4 _MainTex_ST;

struct v2g
{
	float4 pos : SV_POSITION;
	half4 uv : TEXCOORD0;
	float3 normal : TEXCOORD1;
	float angle : TEXCOORD2;
};

struct g2f
{
	float4 pos : SV_POSITION;
	half4 uv : TEXCOORD0;
	float3 normal : TEXCOORD1;
	float angle : TEXCOORD2;
};

v2g vert(appdata_full v)
{
	v2g o;
	UNITY_INITIALIZE_OUTPUT(v2g, o);

	float4 vertex = v.vertex;

	o.normal = UnityObjectToWorldNormal(v.normal);
	float3 absNormal = abs(o.normal);

	o.pos = vertex;

	o.uv = float4(TRANSFORM_TEX(v.texcoord.xy, _MainTex), 1.0, 1.0);


	return o;
}