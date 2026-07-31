#ifndef HORRORGAME_MONSTER_SKIN_FORWARD_INCLUDED
#define HORRORGAME_MONSTER_SKIN_FORWARD_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv          : TEXCOORD0;
    float3 positionWS  : TEXCOORD1;
    half3  normalWS    : TEXCOORD2;
    half4  tangentWS   : TEXCOORD3;
    half3  viewDirWS   : TEXCOORD4;
    half   fogFactor   : TEXCOORD5;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD6;
#endif
    float4 positionCS  : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings MonsterSkinVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.positionWS = positions.positionWS;
    output.positionCS = positions.positionCS;
    output.normalWS = normals.normalWS;
    output.tangentWS = half4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.viewDirWS = GetWorldSpaceViewDir(positions.positionWS);
    output.fogFactor = ComputeFogFactor(positions.positionCS.z);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(positions);
#endif

    return output;
}

void BuildInputData(Varyings input, half3 normalTS, out InputData data)
{
    data = (InputData)0;

    data.positionWS = input.positionWS;
    half3 viewDirWS = SafeNormalize(input.viewDirWS);

    half sgn = input.tangentWS.w;
    half3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);
    data.tangentToWorld = tangentToWorld;
    data.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));
    data.viewDirectionWS = viewDirWS;

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    data.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
    data.shadowCoord = float4(0, 0, 0, 0);
#endif

    data.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    data.vertexLighting = half3(0.0h, 0.0h, 0.0h);
    data.bakedGI = SampleSHPixel(half3(0.0h, 0.0h, 0.0h), data.normalWS);
    data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    data.shadowMask = half4(1, 1, 1, 1);
}

/// A Fresnel term computed from the GEOMETRIC normal, not the mapped one.
///
/// This is the whole reason the creature has its own shader. §04's 관측자 has to see it
/// at GameConstants.ObserverRange = 15 m and §03's beam stops at 12 m, so at the
/// distance the role is defined at there is no light of the player's on it at all. A
/// rim term answers that without answering it with brightness: grazing surfaces pick
/// up the room, faces pointing at the camera stay as dark as they were, so what
/// arrives is an outline rather than a lit creature. §06 keeps its "you notice it too
/// late" — an outline is where something is, not what it is doing.
///
/// From the geometric normal because the alternative was tried in principle and fails
/// in an obvious way: the hide's normal map is 19.6 mm of relief pushed to _BumpScale
/// 1.6, and a Fresnel driven by that lights every pore that happens to face away. At
/// 15 m the creature is about forty pixels tall, so per-texel rim is sub-pixel noise
/// that crawls frame to frame — sparkle on the one thing in the room that has to read
/// as solid. The geometric normal gives a clean, stable edge at any distance.
half MonsterRim(half3 geometricNormalWS, half3 viewDirWS)
{
    half facing = saturate(dot(SafeNormalize(geometricNormalWS), viewDirWS));
    return _RimFloor + (1.0h - _RimFloor) * pow(1.0h - facing, _RimPower);
}

half4 MonsterSkinFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SurfaceData surface;
    InitializeMonsterSurfaceData(input.uv, surface);

    InputData data;
    BuildInputData(input, surface.normalTS, data);

    surface.emission += _RimColor.rgb * (_RimStrength * MonsterRim(input.normalWS, data.viewDirectionWS));

    half4 color = UniversalFragmentPBR(data, surface);

    // Fog, held back on purpose — the third of the three fixes, and the one that makes
    // distance work FOR the creature instead of against it.
    //
    // URP's fog lerps every surface toward unity_FogColor, and docs/ART.md keeps that
    // colour deliberately brighter than the ambient-lit walls so depth reads as haze.
    // At exp² density 0.0333 that is 22% of the pixel at 15 m — and it is the SAME 22%
    // for the creature and for the wall 1.5 m behind it, so it lands on the difference
    // between them and cancels it. Measured: the body and the wall it stood against
    // came back 0.0128 apart out of 1.0, which is three code values, which is nothing.
    //
    // Taking a fraction of that lift breaks the tie the only way it can be broken
    // without adding light: the corridor hazes and the creature does not, so it falls
    // as a darker shape against a lifting background and gets CLEARER with distance.
    // Not zero — a creature with no fog at all is a cut-out pasted onto the picture,
    // and it would be the only object in the game that does not belong to the air.
    half3 fogged = MixFog(color.rgb, data.fogCoord);
    color.rgb = lerp(color.rgb, fogged, _FogResponse);

    color.a = 1.0h;
    return color;
}

#endif
