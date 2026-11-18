using UnityEngine;
using UnityEngine.UI;
using Unity.InferenceEngine;

/// <summary>
/// MiDaS v3.1 DPT BEiT Large 512 ONNX / Unity Inference Engine (Sentis) 2.6.1.
/// Expected input:  [1,3,512,512] float RGB
/// Expected output: [1,512,512] float depth (relative inverse depth)
/// No DFDController integration.
/// </summary>
public class DepthEstimator : MonoBehaviour
{
    [Header("MiDaS DPT BEiT Large 512")]
    [SerializeField] private ModelAsset modelAsset;

    [Header("Input")]
    [SerializeField] private Texture2D inputImage;
    [SerializeField] private bool runOnStart = true;

    [Header("Preview (optional)")]
    [SerializeField] private RawImage depthPreview;
    [SerializeField] private Shader grayscaleShader;

    private const int ModelSize = 384;

    private Worker worker;
    private Tensor<float> inputTensor;
    private RenderTexture modelDepthTexture;
    private RenderTexture depthTexture;
    private Material grayscaleMaterial;

    public Texture2D InputImage => inputImage;
    public RenderTexture DepthTexture => depthTexture;
    public bool HasDepth { get; private set; }

    private void Start()
    {
        if (Initialize() && runOnStart)
            EstimateDepth();
    }

    private bool Initialize()
    {
        if (worker != null)
            return true;

        if (modelAsset == null)
        {
            Debug.LogError("DepthEstimator: Assign dpt_beit_large_512 ONNX ModelAsset.", this);
            return false;
        }

        if (grayscaleShader == null)
            grayscaleShader = Shader.Find("Hidden/DepthEstimator/Grayscale");
        if (grayscaleShader == null)
        {
            Debug.LogError("DepthEstimator: Missing Hidden/DepthEstimator/Grayscale shader.", this);
            return false;
        }

        grayscaleMaterial = new Material(grayscaleShader);
        Model model = ModelLoader.Load(modelAsset);

        // Official MiDaS BEiT preprocessing: RGB from [0,1] to [-1,1]
        // ((rgb - 0.5) / 0.5). Sentis TextureConverter provides [0,1] RGB.
        // Fuse preprocessing and depth-map min/max normalization into GPU graph.
        var graph = new FunctionalGraph();
        var inputs = graph.AddInputs(model);
        var normalizedRgb = (inputs[0] - 0.5f) * 2.0f;
        var prediction = Functional.Forward(model, normalizedRgb)[0];

        // This version assumes ONNX output [1,H,W].
        var min = Functional.ReduceMin(prediction, new[] { 1, 2 }, true);
        var max = Functional.ReduceMax(prediction, new[] { 1, 2 }, true);
        var normalizedDepth = (prediction - min) / (max - min + 1e-6f);
        model = graph.Compile(normalizedDepth);

        worker = new Worker(model, BackendType.GPUCompute);
        inputTensor = new Tensor<float>(new TensorShape(1, 3, ModelSize, ModelSize));
        modelDepthTexture = CreateRenderTexture(ModelSize, ModelSize);

        if (inputImage != null)
            EnsureOutputTexture(inputImage.width, inputImage.height);
        return true;
    }

    private static RenderTexture CreateRenderTexture(int width, int height)
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        rt.Create();
        return rt;
    }

    private void EnsureOutputTexture(int width, int height)
    {
        if (depthTexture != null && depthTexture.width == width && depthTexture.height == height)
            return;

        HasDepth = false;
        if (depthTexture != null)
        {
            depthTexture.Release();
            Destroy(depthTexture);
        }

        depthTexture = CreateRenderTexture(width, height);
        if (depthPreview != null)
            depthPreview.texture = depthTexture;
    }

    [ContextMenu("Estimate Depth")]
    public void EstimateDepth()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("DepthEstimator: Run in Play mode.", this);
            return;
        }
        if (inputImage == null || !Initialize())
        {
            Debug.LogError("DepthEstimator: Input image or model not ready.", this);
            return;
        }

        HasDepth = false;
        EnsureOutputTexture(inputImage.width, inputImage.height);

        // Resizes input to 512x512 (fixed-shape ONNX). The output is later
        // mapped back to the original image size.
        TextureConverter.ToTensor(inputImage, inputTensor, new TextureTransform());
        worker.Schedule(inputTensor);

        // ONNX assumed to output [1,512,512].
        // PeekOutput belongs to Worker; do not Dispose() it.
        var output = worker.PeekOutput() as Tensor<float>;
        if (output == null)
        {
            Debug.LogError("DepthEstimator: Float tensor output was not found.", this);
            return;
        }
        if (output.shape.rank != 3)
        {
            Debug.LogError($"DepthEstimator: Expected [1,H,W], got {output.shape}. Please check this ONNX export's output shape.", this);
            return;
        }

        // TextureConverter expects NCHW. [1,H,W] -> [1,1,H,W]
        output.Reshape(output.shape.Unsqueeze(1));
        TextureConverter.RenderToTexture(
            output,
            modelDepthTexture,
            new TextureTransform().SetCoordOrigin(CoordOrigin.BottomLeft));

        // Existing grayscale shader does a fixed Y-flip. It also copies the
        // 512x512 normalized depth to an RGBA render texture with input resolution.
        Graphics.Blit(modelDepthTexture, depthTexture, grayscaleMaterial);

        if (depthPreview != null)
            depthPreview.texture = depthTexture;

        HasDepth = true;
        Debug.Log($"DepthEstimator (BEiT512): Depth generated at {depthTexture.width}x{depthTexture.height} (inference: 512x512).", this);
    }

    public void SetInputImage(Texture2D image)
    {
        inputImage = image;
        HasDepth = false;
        if (Application.isPlaying && image != null)
            EstimateDepth();
    }

    private void OnDestroy()
    {
        worker?.Dispose();
        inputTensor?.Dispose();
        if (modelDepthTexture != null)
        {
            modelDepthTexture.Release();
            Destroy(modelDepthTexture);
        }
        if (depthTexture != null)
        {
            depthTexture.Release();
            Destroy(depthTexture);
        }
        if (grayscaleMaterial != null)
            Destroy(grayscaleMaterial);
    }
}
