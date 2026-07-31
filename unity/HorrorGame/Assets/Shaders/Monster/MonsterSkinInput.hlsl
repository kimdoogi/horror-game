#ifndef HORRORGAME_MONSTER_SKIN_INPUT_INCLUDED
#define HORRORGAME_MONSTER_SKIN_INPUT_INCLUDED

// Property names are URP Lit's, deliberately and to the letter.
//
// MonsterSkin.cs builds these materials from the generator's manifest, and two
// runtime components — MonsterBeamResolve and MonsterAcquireTell — write into them
// through MaterialPropertyBlocks by shader property ID. Renaming _BumpScale here
// would not fail: the writes would simply land nowhere and the creature would look
// exactly like the day before the component was added.
//
// Everything the material owns must live in ONE UnityPerMaterial block or the SRP
// batcher rejects the shader, and the rejection is a silent performance cliff rather
// than an error.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half4  _EmissionColor;
    half4  _RimColor;
    half   _Cutoff;
    half   _Smoothness;
    half   _Metallic;
    half   _BumpScale;
    half   _OcclusionStrength;
    half   _RimStrength;
    half   _RimPower;
    half   _RimFloor;
    half   _FogResponse;
CBUFFER_END

TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);

/// Reads the generated maps into URP's standard surface description.
///
/// Metallic and smoothness both come out of the packed mask the texture pipeline
/// writes (RGB = metallic, A = smoothness), and both are multiplied by the scalar of
/// the same name rather than replaced by it. That is what lets MonsterBeamResolve roll
/// smoothness off with distance without a second texture.
void InitializeMonsterSurfaceData(float2 uv, out SurfaceData surface)
{
    surface = (SurfaceData)0;

    half4 albedo = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)) * _BaseColor;
    surface.albedo = albedo.rgb;
    surface.alpha  = albedo.a;

    half4 mask = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    surface.metallic   = mask.r * _Metallic;
    surface.smoothness = mask.a * _Smoothness;

    surface.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);

    half occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
    surface.occlusion = LerpWhiteTo(occlusion, _OcclusionStrength);

    surface.emission = SampleEmission(uv, _EmissionColor.rgb,
                                      TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));
    surface.specular = half3(0.0h, 0.0h, 0.0h);
    surface.clearCoatMask = 0.0h;
    surface.clearCoatSmoothness = 0.0h;
}

#endif
