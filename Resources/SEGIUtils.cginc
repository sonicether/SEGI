float3 rgb2hsv(float3 c)
{
    float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, k.wz), float4(c.gb, k.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;

    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 hsv2rgb(float3 c)
{
    float4 k = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + k.xyz) * 6.0 - k.www);
    return c.z * lerp(k.xxx, saturate(p - k.xxx), c.y);
}

float4 DecodeRGBAuint(uint value)
{
    uint ai = value & 0x0000007F;
    uint vi = (value / 0x00000080) & 0x000007FF;
    uint si = (value / 0x00040000) & 0x0000007F;
    uint hi = value / 0x02000000;

    float h = float(hi) / 127.0;
    float s = float(si) / 127.0;
    float v = (float(vi) / 2047.0) * 10.0;
    float a = ai * 2.0;

    v = pow(v, 3.0);

    float3 color = hsv2rgb(float3(h, s, v));

    return float4(color.rgb, a);
}

uint EncodeRGBAuint(float4 color)
{
    //7[HHHHHHH] 7[SSSSSSS] 11[VVVVVVVVVVV] 7[AAAAAAAA]
    float3 hsv = rgb2hsv(color.rgb);
    hsv.z = pow(hsv.z, 1.0 / 3.0);

    uint result = 0;

    uint a = min(127, uint(color.a / 2.0));
    uint v = min(2047, uint((hsv.z / 10.0) * 2047));
    uint s = uint(hsv.y * 127);
    uint h = uint(hsv.x * 127);

    result += a;
    result += v * 0x00000080; // << 7
    result += s * 0x00040000; // << 18
    result += h * 0x02000000; // << 25

    return result;
}

void interlockedAddFloat4(RWTexture3D<uint> destination, int3 coord, float4 value)
{
    uint writeValue = EncodeRGBAuint(value);
    uint compareValue = 0;
    uint originalValue;

    [allow_uav_condition]
    while (true)
    {
        InterlockedCompareExchange(destination[coord], compareValue, writeValue, originalValue);
        if (compareValue == originalValue)
            break;
        compareValue = originalValue;
        float4 originalValueFloats = DecodeRGBAuint(originalValue);
        writeValue = EncodeRGBAuint(originalValueFloats + value);
    }
}

void interlockedAddFloat4b(RWTexture3D<uint> destination, int3 coord, float4 value)
{
    uint writeValue = EncodeRGBAuint(value);
    uint compareValue = 0;
    uint originalValue;

    [allow_uav_condition]
    while (true)
    {
        InterlockedCompareExchange(destination[coord], compareValue, writeValue, originalValue);
        if (compareValue == originalValue)
            break;
        compareValue = originalValue;
        float4 originalValueFloats = DecodeRGBAuint(originalValue);
        writeValue = EncodeRGBAuint(originalValueFloats + value);
    }
}
