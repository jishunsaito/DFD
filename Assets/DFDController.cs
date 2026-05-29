using UnityEngine;

[ExecuteAlways]
public class DFDController : MonoBehaviour
{
    public enum ReferencePlaneMode
    {
        BackDisplay,
        FrontDisplay,
        CustomPlane
    }

    [Header("Input Textures")]
    public Texture colorTexture;
    public Texture depthTexture;

    [Header("Materials")]
    public Material frontMaterial;
    public Material backMaterial;

    [Header("Coordinate System [mm]")]
    [Tooltip("後面ディスプレイ中心を原点とし，手前方向を +Z とする．単位は mm")]
    public Vector3 viewerPositionMm = new Vector3(0f, 0f, 800f);

    [Header("Back Display [mm, px]")]
    public Vector3 backDisplayCenterMm = Vector3.zero;
    public Vector2Int backResolution = new Vector2Int(3840, 2160);
    public Vector2 backPixelPitchMm = new Vector2(0.1845f, 0.1845f);

    [Header("Front Display [mm, px]")]
    public Vector3 frontDisplayCenterMm = new Vector3(0f, 0f, 50f);
    public Vector2Int frontResolution = new Vector2Int(3840, 2160);
    public Vector2 frontPixelPitchMm = new Vector2(0.1845f, 0.1845f);

    [Header("Reference Image Plane")]
    public ReferencePlaneMode referencePlaneMode = ReferencePlaneMode.BackDisplay;

    [Tooltip("CustomPlane 使用時のみ有効．単位は mm")]
    public Vector3 customReferenceCenterMm = Vector3.zero;

    [Tooltip("CustomPlane 使用時のみ有効．単位は mm")]
    public Vector2 customReferenceSizeMm = new Vector2(708f, 396f);

    [Header("Depth Settings")]
    [Tooltip("深度画像で白が近い場合 true．白が遠い場合 false")]
    public bool depthNearIsWhite = true;

    [Range(0f, 1f)]
    public float depthMin = 0f;

    [Range(0f, 1f)]
    public float depthMax = 1f;

    [Tooltip("1より大きくすると前後の輝度差が強調される")]
    public float depthGamma = 1f;

    [Tooltip("true の場合 frontWeight + backWeight = 1 になるように正規化する")]
    public bool normalizeWeights = true;

    [Header("Output")]
    public float brightness = 1f;

    [Tooltip("画像の上下が反転する場合に切り替える")]
    public bool flipY = false;

    [Tooltip("参照画像範囲外の色")]
    public Color outsideColor = Color.black;

    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
    private static readonly int DepthTexID = Shader.PropertyToID("_DepthTex");

    private static readonly int LayerIndexID = Shader.PropertyToID("_LayerIndex");

    private static readonly int ViewerPosMmID = Shader.PropertyToID("_ViewerPosMm");

    private static readonly int DisplayCenterMmID = Shader.PropertyToID("_DisplayCenterMm");
    private static readonly int DisplayResolutionID = Shader.PropertyToID("_DisplayResolution");
    private static readonly int PixelPitchMmID = Shader.PropertyToID("_PixelPitchMm");

    private static readonly int ReferenceCenterMmID = Shader.PropertyToID("_ReferenceCenterMm");
    private static readonly int ReferenceSizeMmID = Shader.PropertyToID("_ReferenceSizeMm");

    private static readonly int BackDisplayZMmID = Shader.PropertyToID("_BackDisplayZMm");
    private static readonly int FrontDisplayZMmID = Shader.PropertyToID("_FrontDisplayZMm");

    private static readonly int DepthNearIsWhiteID = Shader.PropertyToID("_DepthNearIsWhite");
    private static readonly int DepthMinID = Shader.PropertyToID("_DepthMin");
    private static readonly int DepthMaxID = Shader.PropertyToID("_DepthMax");
    private static readonly int DepthGammaID = Shader.PropertyToID("_DepthGamma");
    private static readonly int NormalizeWeightsID = Shader.PropertyToID("_NormalizeWeights");

    private static readonly int BrightnessID = Shader.PropertyToID("_Brightness");
    private static readonly int FlipYID = Shader.PropertyToID("_FlipY");
    private static readonly int OutsideColorID = Shader.PropertyToID("_OutsideColor");

    private void Update()
    {
        Apply();
    }

    private void OnValidate()
    {
        depthGamma = Mathf.Max(0.01f, depthGamma);

        backResolution.x = Mathf.Max(1, backResolution.x);
        backResolution.y = Mathf.Max(1, backResolution.y);
        frontResolution.x = Mathf.Max(1, frontResolution.x);
        frontResolution.y = Mathf.Max(1, frontResolution.y);

        backPixelPitchMm.x = Mathf.Max(0.0001f, backPixelPitchMm.x);
        backPixelPitchMm.y = Mathf.Max(0.0001f, backPixelPitchMm.y);
        frontPixelPitchMm.x = Mathf.Max(0.0001f, frontPixelPitchMm.x);
        frontPixelPitchMm.y = Mathf.Max(0.0001f, frontPixelPitchMm.y);

        customReferenceSizeMm.x = Mathf.Max(0.001f, customReferenceSizeMm.x);
        customReferenceSizeMm.y = Mathf.Max(0.001f, customReferenceSizeMm.y);
    }

    private void Apply()
    {
        GetReferencePlane(
            out Vector3 referenceCenterMm,
            out Vector2 referenceSizeMm
        );

        ApplyToMaterial(
            mat: frontMaterial,
            layerIndex: 0,
            displayCenterMm: frontDisplayCenterMm,
            displayResolution: frontResolution,
            pixelPitchMm: frontPixelPitchMm,
            referenceCenterMm: referenceCenterMm,
            referenceSizeMm: referenceSizeMm
        );

        ApplyToMaterial(
            mat: backMaterial,
            layerIndex: 1,
            displayCenterMm: backDisplayCenterMm,
            displayResolution: backResolution,
            pixelPitchMm: backPixelPitchMm,
            referenceCenterMm: referenceCenterMm,
            referenceSizeMm: referenceSizeMm
        );
    }

    private void ApplyToMaterial(
        Material mat,
        int layerIndex,
        Vector3 displayCenterMm,
        Vector2Int displayResolution,
        Vector2 pixelPitchMm,
        Vector3 referenceCenterMm,
        Vector2 referenceSizeMm
    )
    {
        if (mat == null)
        {
            return;
        }

        if (colorTexture != null)
        {
            mat.SetTexture(MainTexID, colorTexture);
        }

        if (depthTexture != null)
        {
            mat.SetTexture(DepthTexID, depthTexture);
        }

        mat.SetFloat(LayerIndexID, layerIndex);

        mat.SetVector(ViewerPosMmID, viewerPositionMm);

        mat.SetVector(DisplayCenterMmID, displayCenterMm);
        mat.SetVector(
            DisplayResolutionID,
            new Vector4(displayResolution.x, displayResolution.y, 0f, 0f)
        );
        mat.SetVector(PixelPitchMmID, pixelPitchMm);

        mat.SetVector(ReferenceCenterMmID, referenceCenterMm);
        mat.SetVector(ReferenceSizeMmID, referenceSizeMm);

        mat.SetFloat(BackDisplayZMmID, backDisplayCenterMm.z);
        mat.SetFloat(FrontDisplayZMmID, frontDisplayCenterMm.z);

        mat.SetFloat(DepthNearIsWhiteID, depthNearIsWhite ? 1f : 0f);
        mat.SetFloat(DepthMinID, depthMin);
        mat.SetFloat(DepthMaxID, depthMax);
        mat.SetFloat(DepthGammaID, Mathf.Max(0.01f, depthGamma));
        mat.SetFloat(NormalizeWeightsID, normalizeWeights ? 1f : 0f);

        mat.SetFloat(BrightnessID, brightness);
        mat.SetFloat(FlipYID, flipY ? 1f : 0f);
        mat.SetColor(OutsideColorID, outsideColor);
    }

    private void GetReferencePlane(
        out Vector3 referenceCenterMm,
        out Vector2 referenceSizeMm
    )
    {
        switch (referencePlaneMode)
        {
            case ReferencePlaneMode.FrontDisplay:
                referenceCenterMm = frontDisplayCenterMm;
                referenceSizeMm = GetPhysicalSizeMm(frontResolution, frontPixelPitchMm);
                break;

            case ReferencePlaneMode.CustomPlane:
                referenceCenterMm = customReferenceCenterMm;
                referenceSizeMm = customReferenceSizeMm;
                break;

            case ReferencePlaneMode.BackDisplay:
            default:
                referenceCenterMm = backDisplayCenterMm;
                referenceSizeMm = GetPhysicalSizeMm(backResolution, backPixelPitchMm);
                break;
        }
    }

    private static Vector2 GetPhysicalSizeMm(Vector2Int resolution, Vector2 pixelPitchMm)
    {
        return new Vector2(
            resolution.x * pixelPitchMm.x,
            resolution.y * pixelPitchMm.y
        );
    }
}