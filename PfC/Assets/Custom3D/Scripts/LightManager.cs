using UnityEngine;
using UnityEngine.UI;

public class LightManager : MonoBehaviour
{
    public Light[] directionalLights; // Assign 4 directional lights in inspector

    public void SetLightIntensity(float value)
    {
        foreach (Light light in directionalLights)
        {
            light.intensity = value;
        }
    }

    public void SetLightColor(Color newColor)
    {
        foreach (Light light in directionalLights)
        {
            light.color = newColor;
        }
    }

    public void SetLightHue(float hue)
    {
        foreach (Light light in directionalLights)
        {
            Color currentColor = light.color;
            Color.RGBToHSV(currentColor, out float h, out float s, out float v);
            h = hue;
            light.color = Color.HSVToRGB(h, s, v);
        }
    }
}
