Shader "Unlit/DFD"
{
    Properties
    {
        _MainTex ("Color Texture", 2D) = "white" {}
        _DepthTex ("Depth Texture", 2D) = "gray" {}

        _LayerIndex ("Layer Index 0=Front 1=Back", Float) = 0

        _DepthNearIsWhite ("Depth Near Is White", Float) = 1
        _DepthMin ("Depth Min", Float) = 0
        _DepthMax ("Depth Max", Float) = 1
        _DepthGamma ("Depth Gamma", Float) = 1
        _NormalizeWeights ("Normalize Weights", Float) = 1

        _Brightness ("Brightness", Float) = 1
        _FlipY ("Flip Y", Float) = 0
        _OutsideColor ("Outside Color", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "DFDDepthLayerPixelPitch"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_DepthTex);
            SAMPLER(sampler_DepthTex);

            CBUFFER_START(UnityPerMaterial)

            float _LayerIndex;

            float _DepthNearIsWhite;
            float _DepthMin;
            float _DepthMax;
            float _DepthGamma;
            float _NormalizeWeights;

            float _Brightness;
            float _FlipY;
            float4 _OutsideColor;

            float3 _ViewerPosMm;

            float3 _DisplayCenterMm;
            float2 _DisplayResolution;
            float2 _PixelPitchMm;

            float3 _ReferenceCenterMm;
            float2 _ReferenceSizeMm;

            float _BackDisplayZMm;
            float _FrontDisplayZMm;

            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float2 ApplyFlipY(float2 uv)
            {
                if (_FlipY > 0.5)
                {
                    uv.y = 1.0 - uv.y;
                }

                return uv;
            }

            float3 DisplayUVToPhysicalPositionMm(float2 uv)
            {
                float displayWidthMm  = _DisplayResolution.x * _PixelPitchMm.x;
                float displayHeightMm = _DisplayResolution.y * _PixelPitchMm.y;

                float xMm = (uv.x - 0.5) * displayWidthMm;
                float yMm = (uv.y - 0.5) * displayHeightMm;
                float zMm = _DisplayCenterMm.z;

                return float3(
                    _DisplayCenterMm.x + xMm,
                    _DisplayCenterMm.y + yMm,
                    zMm
                );
            }

            float2 ProjectDisplayPointToReferenceUV(float3 displayPointMm)
            {
                float3 rayDir = normalize(displayPointMm - _ViewerPosMm);

                // 全ディスプレイ面と参照面はXY平面に平行とする．
                // 参照面は z = _ReferenceCenterMm.z
                float denom = rayDir.z;

                if (abs(denom) < 1e-6)
                {
                    return float2(-1.0, -1.0);
                }

                float t = (_ReferenceCenterMm.z - _ViewerPosMm.z) / denom;

                if (t <= 0.0)
                {
                    return float2(-1.0, -1.0);
                }

                float3 hitMm = _ViewerPosMm + rayDir * t;

                float2 localMm;
                localMm.x = hitMm.x - _ReferenceCenterMm.x;
                localMm.y = hitMm.y - _ReferenceCenterMm.y;

                float2 refUV;
                refUV.x = localMm.x / max(_ReferenceSizeMm.x, 1e-6) + 0.5;
                refUV.y = localMm.y / max(_ReferenceSizeMm.y, 1e-6) + 0.5;

                return refUV;
            }

            float GetNear01(float depthRaw)
            {
                float range = max(_DepthMax - _DepthMin, 1e-6);
                float depth01 = saturate((depthRaw - _DepthMin) / range);

                // near01 = 1 が手前，0 が奥
                float near01 = (_DepthNearIsWhite > 0.5)
                    ? depth01
                    : 1.0 - depth01;

                return saturate(near01);
            }

            float2 GetDFDWeights(float near01)
            {
                // near01 = 1 : front display
                // near01 = 0 : back display
                float frontWeight = near01;
                float backWeight = 1.0 - near01;

                frontWeight = pow(saturate(frontWeight), _DepthGamma);
                backWeight  = pow(saturate(backWeight),  _DepthGamma);

                if (_NormalizeWeights > 0.5)
                {
                    float sumWeight = max(frontWeight + backWeight, 1e-6);
                    frontWeight /= sumWeight;
                    backWeight  /= sumWeight;
                }

                return float2(frontWeight, backWeight);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 displayUV = ApplyFlipY(input.uv);

                float3 displayPointMm = DisplayUVToPhysicalPositionMm(displayUV);
                float2 sourceUV = ProjectDisplayPointToReferenceUV(displayPointMm);

                if (
                    sourceUV.x < 0.0 || sourceUV.x > 1.0 ||
                    sourceUV.y < 0.0 || sourceUV.y > 1.0
                )
                {
                    return half4(_OutsideColor.rgb, 1.0);
                }

                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sourceUV);
                float depthRaw = SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, sourceUV).r;

                float near01 = GetNear01(depthRaw);
                float2 weights = GetDFDWeights(near01);

                float weight = (_LayerIndex < 0.5)
                    ? weights.x
                    : weights.y;

                float3 outColor = color.rgb * weight * _Brightness;

                return half4(outColor, color.a);
            }

            ENDHLSL
        }
    }
}