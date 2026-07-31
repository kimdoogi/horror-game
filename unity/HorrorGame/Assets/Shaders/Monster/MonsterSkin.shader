// The creature's skin. URP Lit's lighting, plus the two terms that decide whether
// anybody can see it: a Fresnel rim and a per-object fog response.
//
// Why not URP Lit with a cleverer material. Lit has no view-dependent term at all —
// its only additive channel is emission, which is uniform over a surface and therefore
// raises the creature's brightness everywhere it is applied. The measured failure this
// exists to fix is precisely that: an ambient fill of 0.22 pushed the body from 0.004
// BELOW the wall behind it to 0.013 ABOVE it, and both of those are three code values,
// which is invisible in either direction. Adding light to the whole body cannot solve
// it, because the body and the wall are lit by the same ambient and hazed by the same
// fog. Something has to differ from the wall, and an outline is the cheapest honest
// difference there is.
//
// Written rather than Shader Graph: Shader Graph would serialise a JSON asset nothing
// in tools/ regenerates, and every other surface in this project is produced by a
// script that can be re-run.
Shader "HorrorGame/MonsterSkin"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor]   _BaseColor("Colour", Color) = (1,1,1,1)

        _MetallicGlossMap("Metallic (RGB) Smoothness (A)", 2D) = "white" {}
        _Metallic("Metallic", Range(0.0, 1.0)) = 1.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 1.0

        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1.0

        _OcclusionMap("Occlusion", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0

        [HDR] _EmissionColor("Emission", Color) = (0,0,0,1)
        _EmissionMap("Emission Map", 2D) = "white" {}

        [Header(Rim)]
        [HDR] _RimColor("Rim Colour", Color) = (0.62, 0.68, 0.82, 1)
        _RimStrength("Rim Strength", Float) = 0.0
        _RimPower("Rim Falloff", Range(0.5, 12.0)) = 3.0
        _RimFloor("Rim Floor", Range(0.0, 1.0)) = 0.0

        [Header(Atmosphere)]
        _FogResponse("Fog Response", Range(0.0, 1.0)) = 1.0

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
            "Queue" = "Geometry"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MonsterSkinVertex
            #pragma fragment MonsterSkinFragment

            // The creature is a SkinnedMeshRenderer, so skinning has to be declared or
            // it renders in the bind pose — standing to attention in the middle of a
            // chase, with nothing in any log.
            #pragma multi_compile _ _SKINNED_MESH_ENABLED
            #pragma multi_compile_instancing

            // Surface features. _NORMALMAP and _EMISSION are switched on by
            // MonsterSkin.cs; without the keyword URP compiles the sampling out and the
            // maps are assigned, visible in the inspector, and unread.
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _EMISSION

            // §03's beam is a spot, which makes it an ADDITIONAL light — there is no
            // directional light in a basement at night. Dropping the additional-light
            // keywords would leave the creature lit by ambient alone, which is the exact
            // bug being fixed, arriving from the other direction.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_fog

            #include "MonsterSkinInput.hlsl"
            #include "MonsterSkinForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile _ _SKINNED_MESH_ENABLED
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "MonsterSkinInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma multi_compile _ _SKINNED_MESH_ENABLED
            #pragma multi_compile_instancing

            #include "MonsterSkinInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // Required, not optional: HorrorGame_URP_Renderer runs Screen Space Ambient
        // Occlusion with Source = DepthNormals. A shader with no DepthNormals pass is
        // absent from that buffer, so the creature is the one object in the scene that
        // neither receives nor occludes AO — visible as a figure that does not sit in
        // its own contact shadow.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma multi_compile _ _SKINNED_MESH_ENABLED
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS

            #include "MonsterSkinInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    // A creature that falls back to magenta is worse than one that falls back to being
    // lit correctly with no rim, which is exactly what Lit gives — the property names
    // are shared, so every map and every runtime write still lands.
    FallBack "Universal Render Pipeline/Lit"
}
