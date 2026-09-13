using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ColorPickerController : Singleton<ColorPickerController>
{
    [SerializeField] private SVImageController imageController;

    [Header("Values")]
    public float currentHue;
    public float currentSaturation;
    public float currentValue;

    [Header("Images")]
    [SerializeField] private RawImage hueImage;
    [SerializeField] private RawImage saturationImage;
    [SerializeField] private RawImage outputImage;

    [SerializeField] private Slider hueSlider;
    [SerializeField] private TMP_InputField hexInputField;

    private Texture2D hueTexture, saturationTexture, outputTexture;

    [SerializeField] private MeshRenderer testMeshRenderer;

    private void Start()
    {
        Initialize();
    }

    [Button]
    public override void Initialize()
    {
        base.Initialize();

        CreateHueImage();
        CreateSaturationValueImage();
        CreateOutputImage();

        imageController.Initialize();

        UpdateOutputImage();
    }

    private void CreateHueImage()
    {
        hueTexture = new Texture2D(1, 16);
        hueTexture.wrapMode = TextureWrapMode.Clamp;
        hueTexture.name = "HueTexture";

        for (int i = 0; i < hueTexture.height; i++)
        {
            hueTexture.SetPixel(0, i, Color.HSVToRGB((float)i / hueTexture.height, 1, /*0.05f*/ 1));
        }

        hueTexture.Apply();
        currentHue = 0f;

        hueImage.texture = hueTexture;
    }

    private void CreateSaturationValueImage()
    {
        saturationTexture = new Texture2D(16, 16);
        saturationTexture.wrapMode = TextureWrapMode.Clamp;
        saturationTexture.name = "SaturationValueTexture";

        for (int y = 0; y < saturationTexture.height; y++)
        {
            for (int x = 0; x < saturationTexture.width; x++)
            {
                saturationTexture.SetPixel(x, y, Color.HSVToRGB(currentHue, (float)x / saturationTexture.width, (float)y / saturationTexture.height));
            }
        }

        saturationTexture.Apply();
        currentSaturation = 0f;
        currentValue = 0f;

        saturationImage.texture = saturationTexture;
    }

    private void CreateOutputImage()
    {
        outputTexture = new Texture2D(1, 16);
        outputTexture.wrapMode = TextureWrapMode.Clamp;
        outputTexture.name = "OutputTexture";

        Color currentColor = Color.HSVToRGB(currentHue, currentSaturation, currentValue);

        for (int i = 0; i < outputTexture.height; i++)
        {
            outputTexture.SetPixel(0, i, currentColor);
        }

        outputTexture.Apply();
        outputImage.texture = outputTexture;
    }

    private void UpdateOutputImage()
    {
        Color currentColor = Color.HSVToRGB(currentHue, currentSaturation, currentValue);

        for (int i = 0; i < outputTexture.height; i++)
        {
            outputTexture.SetPixel(0, i, currentColor);
        }

        outputTexture.Apply();
        hexInputField.text = ColorUtility.ToHtmlStringRGB(currentColor);

        //FOR TEST
        testMeshRenderer.sharedMaterial.SetColor("_BaseColor", currentColor);
    }

    #region [ SETTERS ]

    public void SetSaturationValue(float saturation, float value)
    {
        currentSaturation = saturation;
        currentValue = value;

        UpdateOutputImage();
    }

    public void UpdateSaturationValueImage()
    {
        currentHue = hueSlider.value;

        for (int y = 0; y < saturationTexture.height; y++)
        {
            for (int x = 0; x < saturationTexture.width; x++)
            {
                saturationTexture.SetPixel(x, y, Color.HSVToRGB(currentHue, (float)x / saturationTexture.width, (float)y / saturationTexture.height));
            }
        }

        saturationTexture.Apply();
        UpdateOutputImage();
    }

    public void OnTextInputChanged()
    {
        if (hexInputField.text.Length < 6) return;

        Color newColor;

        if (ColorUtility.TryParseHtmlString("#" + hexInputField.text, out newColor))
        {
            Color.RGBToHSV(newColor, out currentHue, out currentSaturation, out currentValue);
        }

        hueSlider.value = currentHue;
        hexInputField.text = "";

        UpdateOutputImage();
    }

    #endregion
}