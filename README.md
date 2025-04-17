# TUS Control Room

A Unity environment integrating control over a Media Stream using NDI and websockets. It is multiplayer, allowing different roles in a studio to collaborate.

#### Requirements

- KlakNDI (modified PfC fork)
- NDI 6 installed
- OBS (or VMix?)
- DistroAV installed for OBS
- NDI tools for NDI Bridges
- 1Gb ethernet cable and router for local streaming of NDI output
- Vivox key for voice communication (free with limits)
- XR Interaction Toolkit
- OSC Jack for positional NDI audio
- Netcode for GameObjects
  
## Setup

### Step 1 - Install OBS

Use the following link:
[OBS download](https://obsproject.com/download)

### Step 2 - Install NDI and NDI tools

NDI tools will also download NDI SDK. Use the following link:
[NDI Tools](https://ndi.video/tools/)

### Step 3 - Install DistroAV for OBS

Github link:
[DistroAV releases](https://github.com/DistroAV/DistroAV/releases/tag/6.0.0)

### Step 4 - Generate Vivox Keys 

This can be automatically done using Unity services. Follow the steps on the linked documentation:

[Unity Vivox SDK](https://docs.unity.com/ugs/en-us/manual/vivox-unity/manual/Unity/vivox-unity-first-steps)

### Step 5 - Set up NDI bridge for Multicasting of a Unicast OBS output

- Start NDI Tools (optional)
- Set up private groups using access manager (optional)
- Choose NDI Bridge
- Choose Local Bridge
- Customise the quality and encoding (optional)
- Click Start
- Local devices should be able to see your Bridge with the name written on the page here
- More information on NDI bridges can be found in the [NDI Tools docs](https://docs.ndi.video/all/using-ndi/ndi-tools/ndi-tools-for-windows/bridge)

### Step 6 - Scenes to add to build

- _preload
- ControlRoom

Run _preload, ControlRoom will be loaded by Role select/network connection UI.

## How to test locally

- In the editor, choose the correct Websocket settings based on OBS websocket plugin.
- Build using the previous instructions (network behaviour is different on editor windows so use the builds, can do a developer build if troubleshooting). 
- Have at least two devices, one for the director and one for a journalist.
- Both devices need to be on the same network.
- Both devices must be connected to the internet (for voice communication).
- Run OBS on the director device.
- Run NDI Bridge on the director device.
- On the director device, choose the director in the login screen.
- Select screens, set up OBS outputs, add screens, and turn volume on/off as desired.
- OBS can be controlled from a websocket using the controls UI. Options are transition, record, and stream.
- Turn the NDI sender component on and set up a camera to record the output and send it to OBS.
- On the journalist device, choose the journalist on the login screen.
- You can view the program's output from the NDI Source Selector dropdown.

## Troubleshooting

#### Known issues: 
- NDI stream reliability (mostly NDI related, not Unity)
- Roles aren't yet established completely
- Websockets don't have full functionality
- Error handling with KlakNDI is minimal, so crashes are possible
- Minimal error catching in Websockets

#### To do:
- Scenes are not yet synced for different roles
- Spatial audio for the screens using OSC
- More functionality is required with
- Studio camera implementation and controls

If you don't see the NDI streams in the source selector:
- Check if you have OBS on/or you are on the same network as the Bridge, if you are a journalist
- Check NDI Tools Studio Monitor to see if it is visible there. If not, this might be an NDI installation problem or a local network problem
