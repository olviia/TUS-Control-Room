using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.Experimental.GlobalIllumination;

public class NetworkLightManager : NetworkBehaviour
{
    [Header("UI References")]
    public Slider intensitySlider;
    public Slider hueSlider;
    
    [Header("RGB Color Picker")]
    public Slider rSlider;
    public Slider gSlider;
    public Slider bSlider;
    
    // Add other sliders as needed (saturation, brightness, etc.)
    
    private Light[] directionalLights; 
    
    // Network variables to sync light properties
    private NetworkVariable<float> networkIntensity = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<float> networkHue = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<float> networkSaturation = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<float> networkBrightness = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // RGB Network variables for direct RGB control
    private NetworkVariable<float> networkRed = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<float> networkGreen = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private NetworkVariable<float> networkBlue = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Flag to prevent infinite loops when updating sliders
    private bool isUpdatingFromNetwork = false;

    private void Awake()
    {
        var allLightsByTag = GameObject.FindGameObjectsWithTag("InteractiveLight");
        directionalLights = new Light[allLightsByTag.Length];
        for (int i = 0; i < allLightsByTag.Length; i++)
        {
            directionalLights[i] = allLightsByTag[i].GetComponent<Light>();
        }
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to network variable changes
        networkIntensity.OnValueChanged += OnNetworkIntensityChanged;
        networkHue.OnValueChanged += OnNetworkHueChanged;
        networkSaturation.OnValueChanged += OnNetworkSaturationChanged;
        networkBrightness.OnValueChanged += OnNetworkBrightnessChanged;
        
        // Subscribe to RGB network variable changes
        networkRed.OnValueChanged += OnNetworkRedChanged;
        networkGreen.OnValueChanged += OnNetworkGreenChanged;
        networkBlue.OnValueChanged += OnNetworkBlueChanged;
        
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
            
        // Setup RGB slider listeners
        if (rSlider != null)
            rSlider.onValueChanged.AddListener(SetRedValue);
            
        if (gSlider != null)
            gSlider.onValueChanged.AddListener(SetGreenValue);
            
        if (bSlider != null)
            bSlider.onValueChanged.AddListener(SetBlueValue);
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
        
        // Update both RGB and HSV network values
        networkRed.Value = newColor.r;
        networkGreen.Value = newColor.g;
        networkBlue.Value = newColor.b;
        
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
            
            // Update RGB values when HSV changes
            Color newColor = Color.HSVToRGB(hue, networkSaturation.Value, networkBrightness.Value);
            networkRed.Value = newColor.r;
            networkGreen.Value = newColor.g;
            networkBlue.Value = newColor.b;
        }
        
        // Apply to lights immediately
        ApplyHueToLights(hue);
    }

    // RGB Color Picker Methods
    public void SetRedValue(float red)
    {
        if (IsOwner && !isUpdatingFromNetwork)
        {
            networkRed.Value = red;
            UpdateHSVFromRGB();
        }
        
        ApplyRGBColorToLights();
    }

    public void SetGreenValue(float green)
    {
        if (IsOwner && !isUpdatingFromNetwork)
        {
            networkGreen.Value = green;
            UpdateHSVFromRGB();
        }
        
        ApplyRGBColorToLights();
    }

    public void SetBlueValue(float blue)
    {
        if (IsOwner && !isUpdatingFromNetwork)
        {
            networkBlue.Value = blue;
            UpdateHSVFromRGB();
        }
        
        ApplyRGBColorToLights();
    }

    // Helper method to update HSV values when RGB changes
    private void UpdateHSVFromRGB()
    {
        Color rgbColor = new Color(networkRed.Value, networkGreen.Value, networkBlue.Value);
        Color.RGBToHSV(rgbColor, out float h, out float s, out float v);
        
        // Update HSV network variables (this will trigger their change events)
        networkHue.Value = h;
        networkSaturation.Value = s;
        networkBrightness.Value = v;
    }

    // Additional methods for saturation and brightness
    public void SetLightSaturation(float saturation)
    {
        if (IsOwner && !isUpdatingFromNetwork)
        {
            networkSaturation.Value = saturation;
            
            // Update RGB values when HSV changes
            Color newColor = Color.HSVToRGB(networkHue.Value, saturation, networkBrightness.Value);
            networkRed.Value = newColor.r;
            networkGreen.Value = newColor.g;
            networkBlue.Value = newColor.b;
        }
        
        ApplyCurrentColorToLights();
    }

    public void SetLightBrightness(float brightness)
    {
        if (IsOwner && !isUpdatingFromNetwork)
        {
            networkBrightness.Value = brightness;
            
            // Update RGB values when HSV changes
            Color newColor = Color.HSVToRGB(networkHue.Value, networkSaturation.Value, brightness);
            networkRed.Value = newColor.r;
            networkGreen.Value = newColor.g;
            networkBlue.Value = newColor.b;
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

    // RGB Network variable change handlers
    private void OnNetworkRedChanged(float previousValue, float newValue)
    {
        ApplyRGBColorToLights();
        UpdateSliderValue(rSlider, newValue);
    }

    private void OnNetworkGreenChanged(float previousValue, float newValue)
    {
        ApplyRGBColorToLights();
        UpdateSliderValue(gSlider, newValue);
    }

    private void OnNetworkBlueChanged(float previousValue, float newValue)
    {
        ApplyRGBColorToLights();
        UpdateSliderValue(bSlider, newValue);
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

    private void ApplyRGBColorToLights()
    {
        Color rgbColor = new Color(networkRed.Value, networkGreen.Value, networkBlue.Value);
        ApplyColorToLights(rgbColor);
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
        ApplyRGBColorToLights(); // Use RGB for consistency
        
        // Update sliders without triggering events
        UpdateSliderValue(intensitySlider, networkIntensity.Value);
        UpdateSliderValue(hueSlider, networkHue.Value);
        UpdateSliderValue(rSlider, networkRed.Value);
        UpdateSliderValue(gSlider, networkGreen.Value);
        UpdateSliderValue(bSlider, networkBlue.Value);
    }

    // Public getters for current values (useful for UI initialization)
    public float GetCurrentIntensity() => networkIntensity.Value;
    public float GetCurrentHue() => networkHue.Value;
    public float GetCurrentSaturation() => networkSaturation.Value;
    public float GetCurrentBrightness() => networkBrightness.Value;
    
    // RGB getters
    public float GetCurrentRed() => networkRed.Value;
    public float GetCurrentGreen() => networkGreen.Value;
    public float GetCurrentBlue() => networkBlue.Value;
    
    public Color GetCurrentColor()
    {
        return new Color(networkRed.Value, networkGreen.Value, networkBlue.Value);
    }

    public Color GetCurrentColorHSV()
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