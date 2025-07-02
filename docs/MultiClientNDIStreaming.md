# Multi-Client NDI Streaming
## System Overview

The system enables multiple Unity clients to collaboratively select and stream NDI video sources to each other in real-time. Any client can choose an NDI source (camera, screen capture, etc.) and immediately stream it to all other connected clients via WebRTC, with the selection synchronized across the network.

 ## Core Components
 ### NetworkStreamCoordinator
manages source selection authority using Unity Netcode. When a client selects an NDI source, the server generates a unique session ID and broadcasts the assignment to all clients. This ensures consistent state across the network and handles source transitions cleanly.
 ### WebRTCSignaling
provides a simple message relay system for WebRTC offer/answer exchange and ICE candidate negotiation. It operates as a stateless pass-through, with session management handled by the coordinator layer.
### WebRTCStreamer
manages individual WebRTC peer connections for each pipeline (StudioLive, TVLive). The client selecting a source becomes the "offerer" and streams to all others who become "answerers." Each streamer handles one pipeline and automatically switches between streaming and receiving modes based on network assignments.
### StreamManager
orchestrates the entire system by connecting network events to WebRTC operations. It manages streamer lifecycle, handles source lookup, and coordinates the transition between local NDI display and remote WebRTC streams.

## Data Flow
Source selection flows from UI → NetworkStreamCoordinator → all clients → StreamManager → WebRTCStreamer. The selecting client activates their NDI receiver and begins WebRTC streaming, while other clients disable local NDI and prepare to receive the remote stream. Session IDs ensure all clients participate in the same WebRTC session.

## Key Behaviors
When streaming, a client keeps their NDI receiver active and sends video frames via WebRTC to all other clients. When receiving, clients disable their local NDI receiver and display the incoming WebRTC stream. If no remote stream is active or connection fails, clients automatically fall back to displaying their local NDI source.
The architecture supports multiple concurrent pipelines, allowing different clients to control different screens simultaneously. Source changes trigger clean session transitions with proper WebRTC connection teardown and establishment.