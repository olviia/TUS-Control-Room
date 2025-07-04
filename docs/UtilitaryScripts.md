# Utility Scripts Overview
## TextureNetworkSynchronizer.cs - The Video Stream Engine
Handles the heavy lifting of transmitting video textures over the network. It captures NDI video on the server, compresses and downsamples it for efficiency, then splits large frames into chunks to send to all clients. Clients reassemble the chunks and display the video. This works alongside your WebRTC system as an alternative streaming method.
## AudioSwitcher.cs - The Audio Control Panel
Provides simple audio mode switching for NDI receivers. Users can toggle between "Virtual Speakers" (audio plays through system) and "None" (muted). It uses reflection to access internal NDI settings, giving you UI control over audio routing without rebuilding the receiver.
## ClickForwarder.cs - The Simple Click Handler
A lightweight alternative to BroadcastTriggerClick that only handles mouse clicks (no VR support). It forwards left/right clicks to source objects and pipeline destinations, providing the same basic interaction functionality with less complexity.
## DontDestroyOnLoad.cs - The Scene Persistence Manager
Essential utility that keeps objects alive when switching between Unity scenes. Critical for maintaining your network connections, pipeline states, and any persistent UI elements across scene transitions.
## InstantiateScreen.cs - The Screen Spawner
Allows directors to dynamically create additional preview screens during runtime. Spawns new screens at reduced size in front of the director, useful for creating custom monitoring setups or additional preview windows on demand.
## NDIButtonUI.cs - The Source Selection Interface
Provides a user-friendly dropdown interface for selecting NDI sources. It automatically discovers available NDI streams on the network, creates buttons for each source, and allows users to switch between them. Can auto-select the first available source for convenience.