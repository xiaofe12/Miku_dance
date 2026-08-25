// For more information, visit -> https://github.com/ColinLeung-NiloCat/UnityURPToonLitShaderExample

// This file is intentionally kept in the packaging workspace and synchronized into
// the Unity project before bundle builds. PEAK scene lighting can be much stronger
// than the preview scene, so the final composite clamps accumulated lighting before
// it can bleach character textures to white.

#pragma once

half3 GetMmdSphereColor(ToonLightingData lightingData)
{
    if (_SphereMapMode < 0.5)
    {
        return half3(1,1,1);
    }

    float3 normalVS = normalize(mul((float3x3)UNITY_MATRIX_V, lightingData.normalWS));
    float3 positionVS = mul(UNITY_MATRIX_V, float4(lightingData.positionWS, 1)).xyz;
    float3 eyeVS = normalize(positionVS);
    float3 sphereDir = reflect(eyeVS, normalVS);
    return texCUBE(_SphereCube, sphereDir).rgb;
}

half3 ApplyMmdSphereMap(half3 albedo, ToonLightingData lightingData)
{
    if (_SphereMapMode < 0.5)
    {
        return albedo;
    }

    half3 sphereColor = GetMmdSphereColor(lightingData);
    if (_SphereMapMode < 1.5)
    {
        return albedo * sphereColor;
    }

    if (_SphereMapMode < 2.5)
    {
        return albedo + sphereColor;
    }

    return albedo;
}

half3 SampleMmdToonRamp(half NoL, half shadowAttenuation)
{
    half toonLight = NoL * _ToonTone.y + _ToonTone.z;
    half toonShadow = ((shadowAttenuation - 0.5h) * _ToonTone.x) + _ToonTone.z;
    half rampCoord = saturate(min(toonLight, toonShadow));

    if (_IsFace > 0.5h)
    {
        rampCoord = lerp(0.5h, 1.0h, rampCoord);
    }

    half3 ramp = tex2D(_ToonTex, half2(rampCoord, rampCoord)).rgb;
    ramp = saturate(1.0h - (1.0h - ramp) * _ShadowLum);
    return lerp(_ShadowMapColor, 1.0h.xxx, ramp);
}

half3 GetMmdSpecular(ToonLightingData lightingData, Light light, half distanceAttenuation, bool isAdditionalLight)
{
    if (_Shininess <= 0.0001h || max(_SpecColor.r, max(_SpecColor.g, _SpecColor.b)) <= 0.0001h)
    {
        return 0;
    }

    half3 H = SafeNormalize(light.direction + lightingData.viewDirectionWS);
    half specularStrength = pow(saturate(dot(lightingData.normalWS, H)), _Shininess);
    specularStrength *= light.shadowAttenuation * distanceAttenuation;
    specularStrength *= isAdditionalLight ? 0.18h : 0.55h;
    return saturate(light.color) * _SpecColor.rgb * specularStrength;
}

half3 ShadeGI(ToonSurfaceData surfaceData, ToonLightingData lightingData)
{
    half3 averageSH = SampleSH(0);
    averageSH = max(max(_IndirectLightMinColor, _Ambient.rgb), averageSH);

    half indirectOcclusion = lerp(1, surfaceData.occlusion, 0.5);
    return averageSH * indirectOcclusion;
}

half3 ShadeSingleLight(ToonSurfaceData surfaceData, ToonLightingData lightingData, Light light, bool isAdditionalLight)
{
    half3 N = lightingData.normalWS;
    half3 L = light.direction;

    half NoL = dot(N,L);

    half distanceAttenuation = min(1.35h, light.distanceAttenuation);
    half shadowAttenuation = lerp(1, light.shadowAttenuation, _ReceiveShadowMappingAmount);
    half3 toonRamp = SampleMmdToonRamp(NoL, shadowAttenuation);
    toonRamp *= surfaceData.occlusion;

    half lightWeight = isAdditionalLight ? 0.16h : 0.72h;
    half3 diffuse = saturate(light.color) * toonRamp * distanceAttenuation * lightWeight;
    half3 specular = GetMmdSpecular(lightingData, light, distanceAttenuation, isAdditionalLight);
    return diffuse + specular;
}

half3 ShadeEmission(ToonSurfaceData surfaceData, ToonLightingData lightingData)
{
    half3 emissionResult = lerp(surfaceData.emission, surfaceData.emission * surfaceData.albedo, _EmissionMulByBaseColor);
    return emissionResult;
}

half3 CompositeAllLightResults(half3 indirectResult, half3 mainLightResult, half3 additionalLightSumResult, half3 emissionResult, ToonSurfaceData surfaceData, ToonLightingData lightingData)
{
    half3 rawLightSum = indirectResult + mainLightResult + additionalLightSumResult;
    rawLightSum = min(rawLightSum, 1.12h.xxx);

    half3 baseSurfaceColor = ApplyMmdSphereMap(surfaceData.albedo, lightingData);
    return saturate(baseSurfaceColor * rawLightSum + emissionResult);
}
