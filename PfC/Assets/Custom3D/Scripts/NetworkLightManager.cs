using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class NetworkLightManager : NetworkBehaviour
{
    [Header("Light Setup")]
    public Light[] directionalLights; // Assign 4 directional lights in inspector
    
    [Header("UI References")]
    public Slider intensitySlider;
    public Slider hueSlider;
    // Add other sliders as needed (saturation, brightness, etc.)
    
    // Network variables to sync light properties
    private NetworkVariable<float> networkIntensity = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<float> networkHue = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<float> networkSaturation = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<float> networkBrightness = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Flag to prevent infinite loops when updating sliders
    private bool isUpdatingFromNetwork = false;

    public override void OnNetworkSpawn()
    {
        // Subscribe to network variable changes
        networkIntensity.OnValueChanged += OnNetworkIntensityChanged;
        networkHue.OnValueChanged += OnNetworkHueChanged;
        networkSaturation.OnValueChanged += OnNetworkSaturationChanged;
        networkBrightness.OnValueChanged += OnNetworkBrightnessChanged;
        
        // Setup slider listeners (for all clients, but only owner can change network values)
        SetupSliderListeners();
        
        // Apply initial network values to lights and sliders
        ApplyCurrentNetworkValues();
    }

    private void SetupSliderListeners()
    {
        if (intensitySlider != null)
            intensitySlider.onValueChanged.AddListener(SetLightIntensity);
            
        if (hueSlider != null)
            hueSlider.onValueChanged.AddListener(SetLightHue);
    }

    // Your existing methods, now with network sync
    public void SetLightIntensity(float value)
    {
        // Only allow the owner to change network values
        if (IsOwner && !isUpdatingFromNetwork)
        {
            networkIntensity.Value = value;
        }
        
        // Apply to lights immediately (for responsiveness)
        ApplyIntensityToLights(value);
    }

    public void SetLightColor(Color newColor)
    {
        if (!IsOwner || isUpdatingFromNetwork) return;
        
        // Convert color to HSV for network sync
        Color.RGBToHSV(newColor, out float h, out float s, out float v);
        networkHue.Value = h;
        networkSaturation.Value = s;
        networkBrightness.Value = v;
        
        // Apply immediately
        ApplyColorToLights(newColor);
    }

    public void SetLightHue(float hue)
    {
        if (IsOwner && !isUpdatingFromNetwork)
        {
            networkHue.Value = hue;
        }
        
        // Apply to lights immediately
        ApplyHueToLights(hue);
    }

    // Additional methods for saturation and brightness
    public void SetLightSaturation(float saturation)
    {
        if (IsOwner && !isUpdatingFromNetwork)
        {
            networkSaturation.Value = saturation;
        }
        
        ApplyCurrentColorToLights();
    }

    public void SetLightBrightness(float brightness)
    {
        if (IsOwner && !isUpdatingFromNetwork)
        {
            networkBrightness.Value = brightness;
        }
        
        ApplyCurrentColorToLights();
    }

    // Network variable change handlers
    private void OnNetworkIntensityChanged(float previousValue, float newValue)
    {
        ApplyIntensityToLights(newValue);
        UpdateSliderValue(intensitySlider, newValue);
    }

    private void OnNetworkHueChanged(float previousValue, float newValue)
    {
        ApplyHueToLights(newValue);
        UpdateSliderValue(hueSlider, newValue);
    }

    private void OnNetworkSaturationChanged(float previousValue, float newValue)
    {
        ApplyCurrentColorToLights();
    }

    private void OnNetworkBrightnessChanged(float previousValue, float newValue)
    {
        ApplyCurrentColorToLights();
    }

    // Helper methods for applying values
    private void ApplyIntensityToLights(float intensity)
    {
        foreach (Light light in directionalLights)
        {
            if (light != null)
                light.intensity = intensity;
        }
    }

    private void ApplyColorToLights(Color color)
    {
        foreach (Light light in directionalLights)
        {
            if (light != null)
                light.color = color;
        }
    }

    private void ApplyHueToLights(float hue)
    {
        foreach (Light light in directionalLights)
        {
            if (light != null)
            {
                Color currentColor = light.color;
                Color.RGBToHSV(currentColor, out float h, out float s, out float v);
                h = hue;
                light.color = Color.HSVToRGB(h, s, v);
            }
        }
    }

    private void ApplyCurrentColorToLights()
    {
        Color newColor = Color.HSVToRGB(networkHue.Value, networkSaturation.Value, networkBrightness.Value);
        ApplyColorToLights(newColor);
    }

    private void UpdateSliderValue(Slider slider, float value)
    {
        if (slider != null)
        {
            isUpdatingFromNetwork = true;
            slider.SetValueWithoutNotify(value);
            isUpdatingFromNetwork = false;
        }
    }

    private void ApplyCurrentNetworkValues()
    {
        // Apply all current network values to lights and sliders
        ApplyIntensityToLights(networkIntensity.Value);
        ApplyCurrentColorToLights();
        
        // Update sliders without triggering events
        UpdateSliderValue(intensitySlider, networkIntensity.Value);
        UpdateSliderValue(hueSlider, networkHue.Value);
    }

    // Public getters for current values (useful for UI initialization)
    public float GetCurrentIntensity() => networkIntensity.Value;
    public float GetCurrentHue() => networkHue.Value;
    public float GetCurrentSaturation() => networkSaturation.Value;
    public float GetCurrentBrightness() => networkBrightness.Value;
    
    public Color GetCurrentColor()
    {
        return Color.HSVToRGB(networkHue.Value, networkSaturation.Value, networkBrightness.Value);
    }

    // Method to transfer ownership (optional - useful if you want different clients to control at different times)
    [ServerRpc(RequireOwnership = false)]
    public void RequestOwnershipServerRpc(ulong clientId)
    {
        GetComponent<NetworkObject>().ChangeOwnership(clientId);
    }
}