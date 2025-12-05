# How to Use

## Hardware Required
- 3 laptops
- 1 VR headset
- 1 long cable for headset
- 2 jack headphones with microphones

## Preparation

1. Connect 3 computers to the same network 
2. Computers have labels near keyboard 
3. Connect headset to the director's computer 
4. Run Unity Hub, open PfC project on all 3 computers


> [!NOTE]
> **Don't save the scene 3DLayout** because Unity Netcode requires exactly the same scene to sync. If you saved the scene, go to GitHub Desktop app, left click on scene 3DLayout and discard changes OR commit it, push on one computer, fetch and pull on other computers.

## Setup Each Computer

### 1. Director Computer
1. Open OBS. You can change videos in subscenes and see result in merged scene by following steps:
   - In "Scenes" window scroll down to scenes with name "Subxcene_01_xx" where x is a number from 01 to 16
   - Select subscene which video you want to change and double click on it's media source in "Sources" window
   - Change local path to the video you want by clicking 'Browse' button, click "Open" after selection
   - Click 'OK'

> [!NOTE]
> - Don't put more than one source into one subscene.
> - You can use other types of sources, you are not limited to video.
> - Subtitles are displayed as a placeholder in StudioSuper.

2. Open Unity. 
   - Open scene Assets/Scenes/3DLayout. 
   - Find PointCloudReceiver object in Hierarchy
   - Put IP of presenter computer into it's NetworkPointCloudReader script in the form tcp://ip:4303. Example: tcp://192.168.0.4:4303

### 2. Presenter Computer
- Open head_avatar_commands.txt from the desktop. Insert commands into Windows Power Shell to run Python server
- Open Unity. Open scene Assets/Scenes/3DLayout

### 3. Audience Computer
- Open Unity. Open scene Assets/Scenes/2DPlaceholder

## Starting the Experience

1. Make sure VR headset is in link mode
2. Play Unity on the director computer.
3. Click on **Director** button on login screen. (You can check what IP it connects to by pressing AutoDetect IP in login menu)
> [!NOTE]
> Make a pause of 5 seconds to let server start on director computer before connecting as a presenter
4. Play Unity on the presenter computer. Click on **Presenter** button on login screen. (You can check server's IP by pressing SearchForHost on in login menu)
5. Play Unity on the audience computer. Click on any View button so NDI stream starts playing

> [!NOTE]
> If you restart the experience, please close python server on presenter computer, stop unity on presenter and director (order doesn't matter), then run python server, start director, start presenter (order matters). As python server currently gives data to only one connection, we want director's computer to connect to it first
>Audience unity can be played or stopped anytime

## Gameplay

**As a director**
- you can point controller at screen with different sources (to the left) and press trigger on the right controller. This way video starts playing on "Studio Preview" screen. After that right press trigger on preview screen. Content from studio live screen is streamed now to the screen in 3D studio, and it's audio is played. 
- You can also change lights, add new screens with NDI sources, jump to the studio room and back. Also you can control cameras positions by clicking on controllers. 
- You can communicate with presenter

**As a presenter**
- you can look into webcamera and your avatar will be streamed to the director's computer.
- you can communicate with director and audience can hear you

**As an audience** 
- you can select what view you would like to see by clicking on buttons at the bottom of the screen.
- you can hear presenter talking
