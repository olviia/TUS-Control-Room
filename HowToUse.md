# How to Use

## Hardware Required
- 3 laptops
- 1 VR headset
- 1 long cable for headset
- 2 jack headphones with microphones

## Preparation

1. Connect 3 computers to the same network
2. Connect headset to the director's computer
3. Run Unity Hub, open PfC project on all 3 computers
4. Computers have labels near keyboard

**Important Notes:**
- I temporarily disabled the screens for Twitch streaming, so people don't get confused what the other two screens are for
- **Don't save the scene 3DLayout** because Unity Netcode requires exactly the same scene to sync. If you saved the scene, go to GitHub Desktop app, left click on scene 3DLayout and discard changes OR commit it, push on one computer, fetch and pull on other computer.

## Setup Each Computer

### 1. Director Computer
- Open OBS. You can change videos in subscenes and see result in merged scene
   - In Scenes view scroll down to subscenes
   - Select subscene and double click on it's media source in Sources window
   - Change local path to the video you want by clicking 'Browse' button
   - Click 'OK'
   - Don't put more than one source into one subscene. You can use other types of sources, you are not limited to video. Subtitles are displayed as a placeholder in StudioSuper.
- Open Unity. Open scene 3DLayout. Put IP of presenter computer into PointCloudReceiver

### 2. Presenter Computer
- Open head_avatar_commands.txt from the desktop. Insert commands into terminal to run Python server
- Open Unity. Open scene 3DLayout

### 3. Audience Computer
- Open Unity. Open scene 2DPlaceholder

## Starting the Experience

1. Play Unity on the director computer. Click on **Director** button on login screen. (You can check what IP it connects to by pressing AutoDetect IP)
2. Wait several seconds until server successfully runs on the director's computer
3. Play Unity on the presenter computer. Click on **Presenter** button on login screen. (You can check server's IP by pressing SearchForHost)
4. Play Unity on the audience computer. Click on any button so NDI stream starts playing

If you restart the experience, please start python server on presenter computer, and then play unity on directors computer first. As python server currently gives data to only one connection, we want director's computer to connect to it first

## Gameplay

As a director, you can point at screen with different sources and press trigger on the right controller. Then you can preview it and after that right press trigger on preview screen once more. Content from studio live screen is streamed then to the screen in 3D studio. You can also change lights, add new screens with NDI sources, jump to the studio room and back. Also you can control cameras positions by clicking on controllers. Presenter can communicate with director and audience using Vivox voice chat. Audience can hear presenter, but cannot hear director. 

As a presenter, you can look into webcamera and your avatar will be streamed to the director's computer.

As an audience, you can select what view you would like to see by clicking on buttons at the bottom of the screen.
