using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DFDUIController : MonoBehaviour
{
    [Header("Target")]
    public DFDController dfdController;

    [Header("UI Root")]
    [Tooltip("Canvas か，ViewerZ / Space / Gamma / Y をまとめている親オブジェクトを入れる")]
    public GameObject uiRoot;

    [Header("Viewer Z [mm]")]
    public Slider viewerZSlider;
    public TMP_InputField viewerZInput;

    private float viewerZMin = 0f;
    private float viewerZMax = 2000f;

    [Header("Display Space [mm]")]
    public Slider spaceSlider;
    public TMP_InputField spaceInput;

    private float spaceMin = 0f;
    private float spaceMax = 200f;

    [Header("Depth Gamma")]
    public Slider gammaSlider;
    public TMP_InputField gammaInput;

    private float gammaMin = 0.1f;
    private float gammaMax = 5f;

    [Header("Front Display Y [mm]")]
    [Tooltip("前面ディスプレイ中心のY座標．後面ディスプレイ中心を0 mmとする")]
    public Slider ySlider;
    public TMP_InputField yInput;

    private float yMin = -1000f;
    private float yMax = 1000f;


    private int decimalPlaces = 1;


    private bool ignoreToggleWhileInputFocused = true;

    private bool isSyncing;

    private void Awake()
    {
        SetupSliders();
        RegisterEvents();
        PullValuesFromDFDController();
    }

    private void Update()
    {
        HandleToggleUI();
    }

    private void SetupSliders()
    {
        if (viewerZSlider != null)
        {
            viewerZSlider.minValue = viewerZMin;
            viewerZSlider.maxValue = viewerZMax;
        }

        if (spaceSlider != null)
        {
            spaceSlider.minValue = spaceMin;
            spaceSlider.maxValue = spaceMax;
        }

        if (gammaSlider != null)
        {
            gammaSlider.minValue = gammaMin;
            gammaSlider.maxValue = gammaMax;
        }

        if (ySlider != null)
        {
            ySlider.minValue = yMin;
            ySlider.maxValue = yMax;
        }
    }

    private void RegisterEvents()
    {
        if (viewerZSlider != null)
        {
            viewerZSlider.onValueChanged.AddListener(OnViewerZSliderChanged);
        }

        if (spaceSlider != null)
        {
            spaceSlider.onValueChanged.AddListener(OnSpaceSliderChanged);
        }

        if (gammaSlider != null)
        {
            gammaSlider.onValueChanged.AddListener(OnGammaSliderChanged);
        }

        if (ySlider != null)
        {
            ySlider.onValueChanged.AddListener(OnYSliderChanged);
        }

        if (viewerZInput != null)
        {
            viewerZInput.onEndEdit.AddListener(OnViewerZInputEndEdit);
        }

        if (spaceInput != null)
        {
            spaceInput.onEndEdit.AddListener(OnSpaceInputEndEdit);
        }

        if (gammaInput != null)
        {
            gammaInput.onEndEdit.AddListener(OnGammaInputEndEdit);
        }

        if (yInput != null)
        {
            yInput.onEndEdit.AddListener(OnYInputEndEdit);
        }
    }

    private void PullValuesFromDFDController()
    {
        if (dfdController == null)
        {
            return;
        }

        float viewerZ = dfdController.viewerPositionMm.z;
        float space = dfdController.frontDisplayCenterMm.z - dfdController.backDisplayCenterMm.z;
        float gamma = dfdController.depthGamma;
        float y = dfdController.viewerPositionMm.y;

        SetViewerZ(viewerZ);
        SetSpace(space);
        SetGamma(gamma);
        SetY(y);
    }

    private void HandleToggleUI()
    {
        if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            return;
        }

        if (ignoreToggleWhileInputFocused && IsInputFieldFocused())
        {
            return;
        }

        if (uiRoot != null)
        {
            uiRoot.SetActive(!uiRoot.activeSelf);
        }
    }

    private bool IsInputFieldFocused()
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        GameObject selected = EventSystem.current.currentSelectedGameObject;

        if (selected == null)
        {
            return false;
        }

        return selected.GetComponent<TMP_InputField>() != null;
    }

    private void OnViewerZSliderChanged(float value)
    {
        if (isSyncing)
        {
            return;
        }

        SetViewerZ(value);
    }

    private void OnSpaceSliderChanged(float value)
    {
        if (isSyncing)
        {
            return;
        }

        SetSpace(value);
    }

    private void OnGammaSliderChanged(float value)
    {
        if (isSyncing)
        {
            return;
        }

        SetGamma(value);
    }

    private void OnYSliderChanged(float value)
    {
        if (isSyncing)
        {
            return;
        }

        SetY(value);
    }

    private void OnViewerZInputEndEdit(string text)
    {
        if (TryParseFloat(text, out float value))
        {
            SetViewerZ(value);
        }
        else
        {
            RefreshViewerZUI();
        }
    }

    private void OnSpaceInputEndEdit(string text)
    {
        if (TryParseFloat(text, out float value))
        {
            SetSpace(value);
        }
        else
        {
            RefreshSpaceUI();
        }
    }

    private void OnGammaInputEndEdit(string text)
    {
        if (TryParseFloat(text, out float value))
        {
            SetGamma(value);
        }
        else
        {
            RefreshGammaUI();
        }
    }

    private void OnYInputEndEdit(string text)
    {
        if (TryParseFloat(text, out float value))
        {
            SetY(value);
        }
        else
        {
            RefreshYUI();
        }
    }

    private void SetViewerZ(float value)
    {
        if (dfdController == null)
        {
            return;
        }

        value = Mathf.Clamp(value, viewerZMin, viewerZMax);

        Vector3 viewer = dfdController.viewerPositionMm;
        viewer.z = value;
        dfdController.viewerPositionMm = viewer;

        SyncViewerZUI(value);
    }

    private void SetSpace(float value)
    {
        if (dfdController == null)
        {
            return;
        }

        value = Mathf.Clamp(value, spaceMin, spaceMax);

        Vector3 front = dfdController.frontDisplayCenterMm;

        // 後面ディスプレイのzを基準に，前面ディスプレイを手前側へ配置
        front.z = dfdController.backDisplayCenterMm.z + value;

        dfdController.frontDisplayCenterMm = front;

        SyncSpaceUI(value);
    }

    private void SetGamma(float value)
    {
        if (dfdController == null)
        {
            return;
        }

        value = Mathf.Clamp(value, gammaMin, gammaMax);

        dfdController.depthGamma = value;

        SyncGammaUI(value);
    }

    private void SetY(float value)
    {
        if (dfdController == null)
        {
            return;
        }

        value = Mathf.Clamp(value, yMin, yMax);

        Vector3 front = dfdController.frontDisplayCenterMm;

        // 後面ディスプレイ中心を y = 0 としたときの，前面ディスプレイ中心の高さ
        front.y = value;

        dfdController.frontDisplayCenterMm = front;

        SyncYUI(value);
    }

    private void RefreshViewerZUI()
    {
        if (dfdController == null)
        {
            return;
        }

        SyncViewerZUI(dfdController.viewerPositionMm.z);
    }

    private void RefreshSpaceUI()
    {
        if (dfdController == null)
        {
            return;
        }

        float space = dfdController.frontDisplayCenterMm.z - dfdController.backDisplayCenterMm.z;
        SyncSpaceUI(space);
    }

    private void RefreshGammaUI()
    {
        if (dfdController == null)
        {
            return;
        }

        SyncGammaUI(dfdController.depthGamma);
    }

    private void RefreshYUI()
    {
        if (dfdController == null)
        {
            return;
        }

        SyncYUI(dfdController.frontDisplayCenterMm.y);
    }

    private void SyncViewerZUI(float value)
    {
        isSyncing = true;

        if (viewerZSlider != null)
        {
            viewerZSlider.SetValueWithoutNotify(value);
        }

        if (viewerZInput != null)
        {
            viewerZInput.SetTextWithoutNotify(FormatFloat(value));
        }

        isSyncing = false;
    }

    private void SyncSpaceUI(float value)
    {
        isSyncing = true;

        if (spaceSlider != null)
        {
            spaceSlider.SetValueWithoutNotify(value);
        }

        if (spaceInput != null)
        {
            spaceInput.SetTextWithoutNotify(FormatFloat(value));
        }

        isSyncing = false;
    }

    private void SyncGammaUI(float value)
    {
        isSyncing = true;

        if (gammaSlider != null)
        {
            gammaSlider.SetValueWithoutNotify(value);
        }

        if (gammaInput != null)
        {
            gammaInput.SetTextWithoutNotify(FormatFloat(value));
        }

        isSyncing = false;
    }

    private void SyncYUI(float value)
    {
        isSyncing = true;

        if (ySlider != null)
        {
            ySlider.SetValueWithoutNotify(value);
        }

        if (yInput != null)
        {
            yInput.SetTextWithoutNotify(FormatFloat(value));
        }

        isSyncing = false;
    }

    private bool TryParseFloat(string text, out float value)
    {
        text = text.Trim();

        return float.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    private string FormatFloat(float value)
    {
        return value.ToString("F" + decimalPlaces, CultureInfo.InvariantCulture);
    }
}