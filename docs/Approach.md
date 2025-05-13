# Approach
## Current Implementation

There are two scenes at the moment, _preload and ControlRoom. First plays the _preload scene and then goes ControlRoom.
## _preload Scene
Has to be loaded first. Contains next elements:
- XR Interaction Setup. There are the controllers and camera that are necessary for VR build. They have Don'tDestroyOnLoad component so they are transfered to the next Scene without the need to create new ones.
- XR Device Simulator. Good for testing on PC, but has to be deactivated for HeadSet testing
- NetworkManager. Unity Netcode component that is responcible for Client-Server connection through networks
- LoginUi. Ui element with buttons and input field. Currently Director button runs the Server and all other buttons run the Client. Server has to be run before Client, otherwice Client has nothing to connect to and disconnects as a result. Input field is for IP address. If left blank, then the default value 127.0.0.1 is applied
- SceneManager. Listens to the Network events and when Network spawns, loads the ControlRoom Scene. Aplies different layermasks to Server and Client so Client cannot see and interract with server's scene components and vice versa
- CommManager. Launches Vivox chat. Vivox has to have an access to the Internet to actually run and also the credentials must be generated first for each new instence of this project. Currently has just one group chat for everybody connected

 ## ControlRoom Scene
 ### Director View
 - Every component incside is in Director layer, is shown on the Server side
 - GrabbableUI. Contains 3 buttons that allow to control OBS on that device through Unity. Also had an Add button to generate a new Screen prefab for another media source
 - WebSocketManager. Has methods that are triggered from GrabbableUI buttons. Commmunicates with OBS though WebSocket, has to have same settings for WebSocket connection as OBS
 - Program. Is basically a prefab that is called Screen and is located in Prefab folder. Has a Quad Mesh as the surface for the media source and NDIReceiver
 - NDIReceiver is a component that gets the media source from OBS and converts it in Unity acceptable format. Sometimes doesn't run smoothly and requires a bit more of work
 - There are UI buttons inside the Quad component. Frist gets all available NDI sources, second refreshes the sources if there were any changes, and third switches the audio on and off
### Studio View
- Every component incside is on Studio layer, is shown on the Client side
- Currently there is the same Screen prefab as on the Director layer, but later it has to be changed to another mesh that shows what Director chooses
### Audience View 
- Currently is also in Studio layer. Has an experiental component that grabs the frames from first excisting NDIReceiver on Director view when the Server is running and sends it to Quad in Audience View when it is the Client using Unity Netcode. Currently transfers only texture, no audio. Works good enough with fast internet but becomes very laggy when the Internet is worse
