using System;

namespace Util.MergedScreen
{
    public interface IMergedScreenSelectionButton
    {
        public static event Action<string, IMergedScreenSelectionButton> OnMergedScreenButtonClicked;
        public static void TriggerEvent(string sceneName, IMergedScreenSelectionButton button)
        {
            OnMergedScreenButtonClicked?.Invoke(sceneName, button);
        }
    }
}