using System;

public interface IScreensCommunication
{
    
    //send to 
    public static event Action<string> OnSendToStudio;
    public static void InvokeSendToStudio(string text) => OnSendToStudio?.Invoke(text);
    
    public static event Action<string> OnSendToStudioPreview;
    public static void InvokeSendToStudioPreview(string text) => OnSendToStudioPreview?.Invoke(text);

}
