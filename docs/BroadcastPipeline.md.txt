## Script Overview & Connections
### BroadcastTriggerClick.cs - The Input Handler
Acts as the universal click detector for both VR controllers and mouse input. When you trigger/click on objects, it identifies whether it's a left or right action and forwards the command to either a source or destination object.
### SourceObject.cs - The Video Source
Represents individual NDI video sources (cameras, screen captures, etc.) in your scene. Each source registers itself with the pipeline manager and responds to clicks by requesting assignment to specific broadcast pipelines.
### PipelineDestination.cs - The Output Screens
Represents the destination screens where video gets displayed (StudioLive, TVLive, etc.). These are the endpoints that receive and display the selected video sources, and they handle "go live" commands.
### BroadcastPipelineManager.cs - The Local Director
The central orchestrator that manages all local broadcast operations. It handles:

- Source-to-pipeline assignments (left click = TV Preview, right click = Studio Preview)
- Visual feedback through colored outlines
- Forwarding content from preview to live stages
- Coordinating with the network system for multi-client streaming

### PipelineTypes.cs - The Pipeline Definitions
Simple enum defining the four broadcast stages: StudioPreview, StudioLive, TVPreview, TVLive.

----------------------

Local Operations: 
BroadcastPipelineManager handles immediate local assignments and visual feedback
Network Coordination: When content goes "live" (StudioLive/TVLive), it requests network control through the coordinator
Stream Management: The system switches between local NDI display and remote WebRTC streams based on who has control
Visual Feedback: Outlines change color to indicate local vs. network-controlled pipelines

The architecture allows multiple directors to collaboratively control different pipelines simultaneously - one person might control Studio while another controls TV, with real-time synchronization across all connected clients.