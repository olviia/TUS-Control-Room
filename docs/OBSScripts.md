# OBS Integration Scripts Overview

Scripts in Legasy folder should be rewritten to use this architecture

## ObsOperationBase.cs - The OBS Foundation
The parent class that all OBS operations inherit from. It handles the core OBS WebSocket connection, provides validation methods, error handling, and common utilities. Think of it as the "OBS connection manager" that ensures all other OBS scripts can safely communicate with OBS Studio.
## ObsUtilities.cs - The OBS Toolkit
A static utility library containing all the common OBS operations like creating scenes, adding NDI sources, managing filters, and finding content. It's like a Swiss Army knife for OBS - any script can use these pre-built functions to interact with OBS without writing complex WebSocket code.
ObsSceneSourceOperation.cs - The Scene Compositor
Adds one OBS scene as a source inside another scene (like picture-in-picture). When your broadcast system goes "live," this script can automatically add the selected content to your main streaming scene, positioning it at the bottom layer or a specific position.
## ObsFinder.cs - The OBS Search Engine
A specialized utility for finding scenes and sources in OBS based on filter properties. It's particularly useful for locating scenes that contain specific NDI outputs, helping your system automatically discover where content should be routed.
## ObsNdiSourceOperation.cs - The NDI Bridge Builder
Creates the connection between Unity and OBS by setting up NDI sources in OBS scenes. It can automatically detect Unity's NDI sender, create the appropriate OBS scene and source, and optionally add an NDI output filter to re-broadcast the content with a new name.

# How They Connect to the Broadcast System
These OBS scripts integrate with the multi-client streaming architecture in several key ways:
Pipeline Integration: When content moves to "Live" stages in the BroadcastPipelineManager, the OBS scripts automatically:

Find the appropriate OBS scene using ObsFinder
Add the selected source to the live stream using ObsSceneSourceOperation
Manage NDI routing between Unity and OBS via ObsNdiSourceOperation

Network Coordination: The OBS operations work alongside your NetworkStreamCoordinator to ensure that when multiple directors collaborate, OBS scenes are updated consistently across all clients.
Content Flow: The system creates a seamless pipeline: Unity NDI sources → Your Pipeline Manager → OBS Scenes → Final Broadcast Output, with automatic scene switching and source management.
This creates a professional broadcast workflow where directors can collaboratively select content in Unity, and it automatically appears in the correct OBS scenes for live streaming, with full multi-client synchronization and fallback handling.
The architecture allows for complex broadcast scenarios like having different OBS scenes for different pipeline types (Studio vs TV) while maintaining network coordination between multiple directors.
