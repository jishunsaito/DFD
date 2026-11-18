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

        _BackgroundThreshold ("Background Threshold", Range(0,1)) = 0.12
        _BackgroundFeather ("Background Feather", Range(0.0001,0.3)) = 0.03
        _DepthBlurRadiusPx ("Depth Blur Radius Px", Range(0,4)) = 1
        _DepthBilateralSigma ("Depth Bilateral Sigma", Range(0.001,1)) = 0.08

        _BackOcclusionThreshold ("Back Occlusion Threshold", Range(0,1)) = 0.6
        _BackOcclusionFeather ("Back Occlusion Feather", Range(0.001,0.3)) = 0.08
        _KillBackNearPixels ("Kill Back Near Pixels", Float) = 1
        _BackEdgeExpandPx ("Back Edge Expand Radius (Depth Pixels)", Range(0,8)) = 1
        _BackEdgeExpandStrength ("Back Edge Expand Strength", Range(0,1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "DFDDepthLayerPixelPitch"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _DepthTex;
            float4 _DepthTex_TexelSize;

            float _LayerIndex;

            float _DepthNearIsWhite;
            float _DepthMin;
            float _DepthMax;
            float _DepthGamma;
            float _NormalizeWeights;

            float _Brightness;
            float _FlipY;
            float4 _OutsideColor;

            float _BackgroundThreshold;
            float _BackgroundFeather;
            float _DepthBlurRadiusPx;
            float _DepthBilateralSigma;

            float _BackOcclusionThreshold;
            float _BackOcclusionFeather;
            float _KillBackNearPixels;
            float _BackEdgeExpandPx;
            float _BackEdgeExpandStrength;

            float3 _ViewerPosMm;

            float3 _DisplayCenterMm;
            float2 _DisplayResolution;
            float2 _PixelPitchMm;

            float3 _ReferenceCenterMm;
            float2 _ReferenceSizeMm;

            float _BackDisplayZMm;
            float _FrontDisplayZMm;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
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

            float GetDepth01(float depthRaw)
            {
                float range = max(_DepthMax - _DepthMin, 1e-6);
                return saturate((depthRaw - _DepthMin) / range);
            }

            float GetNear01FromDepth01(float depth01)
            {
                float near01 = (_DepthNearIsWhite > 0.5)
                    ? depth01
                    : 1.0 - depth01;

                return saturate(near01);
            }

            float GetNear01(float depthRaw)
            {
                return GetNear01FromDepth01(GetDepth01(depthRaw));
            }

            float SampleDepth01(float2 uv)
            {
                float depthRaw = tex2D(_DepthTex, uv).r;
                return GetDepth01(depthRaw);
            }

            float SampleNear01(float2 uv)
            {
                float depthRaw = tex2D(_DepthTex, uv).r;
                return GetNear01(depthRaw);
            }

            float SampleBlurredDepth01(float2 uv)
            {
                if (_DepthBlurRadiusPx <= 0.001)
                {
                    return SampleDepth01(uv);
                }

                float2 t = _DepthTex_TexelSize.xy * _DepthBlurRadiusPx;
                float center = SampleDepth01(uv);
                float sigma = max(_DepthBilateralSigma, 1e-6);

                float sum = 0.0;
                float wsum = 0.0;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 suv = uv + float2((float)x * t.x, (float)y * t.y);
                        float d = SampleDepth01(suv);
                        float diff = d - center;
                        float w = exp(-(diff * diff) / (2.0 * sigma * sigma));

                        if (x == 0 && y == 0)
                        {
                            w *= 2.0;
                        }

                        sum += d * w;
                        wsum += w;
                    }
                }

                return (wsum > 1e-6) ? (sum / wsum) : center;
            }

            float GetBackgroundMask(float depth01)
            {
                // 元の depth01 が小さいほど背景寄りとみなす
                return 1.0 - smoothstep(
                    _BackgroundThreshold - _BackgroundFeather,
                    _BackgroundThreshold + _BackgroundFeather,
                    depth01
                );
            }

            float2 GetDFDWeights(float near01)
            {
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

            // Only expand the near-object mask for the back display.
            // A 3x3 maximum filter covers residual edge halos.
            float SampleExpandedNear01(float2 uv)
            {
                float center = SampleNear01(uv);
                if (_BackEdgeExpandPx <= 0.001 || _BackEdgeExpandStrength <= 0.001)
                    return center;

                float2 delta = _DepthTex_TexelSize.xy * _BackEdgeExpandPx;
                float maximum = center;
                [unroll]
                for (int y = -1; y <= 1; ++y)
                {
                    [unroll]
                    for (int x = -1; x <= 1; ++x)
                    {
                        float2 pos = saturate(uv + float2(x, y) * delta);
                        maximum = max(maximum, SampleNear01(pos));
                    }
                }
                return lerp(center, maximum, saturate(_BackEdgeExpandStrength));
            }

            float GetBackOcclusionMask(float near01)
            {
                return 1.0 - smoothstep(
                    _BackOcclusionThreshold - _BackOcclusionFeather,
                    _BackOcclusionThreshold + _BackOcclusionFeather,
                    near01
                );
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 displayUV = ApplyFlipY(input.uv);

                float3 displayPointMm = DisplayUVToPhysicalPositionMm(displayUV);
                float2 sourceUV = ProjectDisplayPointToReferenceUV(displayPointMm);

                if (
                    sourceUV.x < 0.0 || sourceUV.x > 1.0 ||
                    sourceUV.y < 0.0 || sourceUV.y > 1.0
                )
                {
                    return fixed4(_OutsideColor.rgb, 1.0);
                }

                fixed4 color = tex2D(_MainTex, sourceUV);

                float depth01 = SampleBlurredDepth01(sourceUV);
                float near01 = GetNear01FromDepth01(depth01);
                float backgroundMask = GetBackgroundMask(depth01);
                float foregroundMask = 1.0 - backgroundMask;

                // 背景は front/back どちらでも真っ黒にする
                if (backgroundMask >= 0.999)
                {
                    return fixed4(0.0, 0.0, 0.0, 1.0);
                }

                float2 weights = GetDFDWeights(near01);

                if (_LayerIndex >= 0.5 && _KillBackNearPixels > 0.5)
                {
                    float expandedNear = max(near01, SampleExpandedNear01(sourceUV));
                    float backMask = GetBackOcclusionMask(expandedNear);
                    weights.y *= backMask;
                }

                float weight = (_LayerIndex < 0.5)
                    ? weights.x
                    : weights.y;

                // 物体部分のみ表示。背景境界は smoothstep でなめらかに落とす
                float alpha = saturate(weight) * foregroundMask;
                float3 outColor = color.rgb * _Brightness;

                // 背景へ自然にフェードさせる
                outColor *= foregroundMask;

                return fixed4(outColor, alpha);
            }

            ENDCG
        }
    }

    FallBack Off
}
