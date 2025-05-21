using UnityEngine;
using UnityEngine.UI;

public class RGBColorPicker : MonoBehaviour
{
    public Slider rSlider;
    public Slider gSlider;
    public Slider bSlider;

    public LightManager lightManager;

    void Start()
    {
        rSlider.onValueChanged.AddListener(UpdateColor);
        gSlider.onValueChanged.AddListener(UpdateColor);
        bSlider.onValueChanged.AddListener(UpdateColor);
    }

    void UpdateColor(float _)
    {
        Color color = new Color(rSlider.value, gSlider.value, bSlider.value);
        lightManager.SetLightColor(color);
    }
}
