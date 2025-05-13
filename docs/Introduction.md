# Abstract
Current Name: TUS Control Room
Technologies: Unity 2022.3.49, C#, OBS 31 + DitroAV plugin, NDI 6, Vivox voice chat, Customized PrefrontalCortex KlakNDI https://github.com/prefrontalcortex/KlakNDI, Unity Netcode for Game Objects

Idea: implement an application that allows VR news creation and consumption experience

Current Architecture Idea:

                                            Screen
        [Director]  ----------->  | [Reporter] + [Guest] | \
                                  | -------------------- |  \  Scene    
                                  |           ^          |  / 
                                  |      [Audience]      | /
                                       
- Director is responsible for preparing and selecting the media source to show in real time. Director is separate from the Studio. Director can observe what is happening in the Studio and can talk to Reporter.
- Studio is a virtual scene that has Reporter, Guest, Audience and the Screen
- Screen is a a placeholder for the content that is selected by the Director. Screen content dynamically changes when Director selects another source.  
- Reporter is responsible for delivering news or conducting interviews. Reporter is located in the Studio. Reporter can hear the Director and can talk with Guest.
- Guest is an invited person eg. celebrity or expert. Guest is located in the Studio. Guest can talk with Reporter. Guest can not hear the Director
- Audience is located in the Studio. Audience can hear both Reporter and Guest, but can not talk to them.

        
        

