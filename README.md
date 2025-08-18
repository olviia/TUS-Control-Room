# TUS Control Room

A Unity environment integrating control over a Media Stream using NDI and websockets. It is multiplayer, allowing different roles in a studio to collaborate.

#### Requirements

- KlakNDI (modified PrefrontalCortex fork, is included in packages in this project)
- NDI 6 installed
- OBS (or VMix?)
- DistroAV installed for OBS
- NDI tools for NDI Bridges (this may be ignored if you don't need NDI bridge)
- 1Gb ethernet cable and router for local streaming of NDI output (may be less if you are ik with more lags XD)
- Vivox key for voice communication (free with limits)
- XR Interaction Toolkit
- OSC Jack for positional NDI audio
- Netcode for GameObjects

### Documentation
https://github.com/olviia/TUS-Control-Room/blob/main/docs/Introduction.md
  
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


### Step 5 - Scenes to run

Scene name is **3DLayout**, this scene is in Test folder

## How to test locally

1. Run OBS on host device (or on both, it's ok)
2. Add scenes ObsScenes.json
3. in OBS websocket settings set port number the same as in WebsocketManager in Unity
4. Run several instances on machines that are connected to the same network.
5. It is possible to use ParrelSync for that, just make sure that the first director (host) is launched from the original editor
6. To launch the host, enter your IP or click button 'AutoDetect', then click 'Director'
7. To launch the client, click button 'SearchForHost', then click 'Director'
8. 'Audience' isn't implemented yet
9. Both devices must be connected to the internet (for voice communication).
11. Select screens, set up OBS outputs, add screens, and turn volume on/off as desired.
12. OBS can be controlled from a WebSocket using the controls UI. Options are transition, record, stream, audio.
15. Add the screen, select the source from the NDI Source Selector dropdown.
16. Right click on source is for the studio screen pipeline
17. Left click on soure is for the TV screen pipeline

## Troubleshooting

#### Known issues: 
- NDI stream bufferization issue visible in log
- Websockets don't have full functionality, tested only on host machine
- WebRtc streaming is not fully implemented yet
- Tested on two users

If you don't see the NDI streams in the source selector:
- Check if you have OBS on/or you are on the same network as the Bridge, if you are a journalist
- Check NDI Tools Studio Monitor to see if it is visible there. If not, this might be an NDI installation problem or a local network problem

Vivox troubleshooting:

Internet connection with required ports open. Check Vivox documentation for more information: [required ports](https://support.unity.com/hc/en-us/articles/4407491745940-Vivox-What-IPs-and-ports-are-required-for-Vivox-to-work), [Integration](https://docs.unity.com/ugs/manual/lobby/manual/vivox-integration)
