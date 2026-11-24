using UnityEngine;

public sealed class AppearancePreferenceDriver : MonoBehaviour
{
    private float nextPoll;
    private void Update()
    {
        if (Time.unscaledTime >= nextPoll)
        {
            nextPoll = Time.unscaledTime + 2f;
            if (AppearancePreference.Mode == AppearancePreference.Auto) AppearancePreference.RefreshDevice();
        }
        // A cold texture/shader load must not consume the entire fade in one stalled frame.
        AppearancePreference.Tick(Mathf.Min(Time.unscaledDeltaTime,.05f));
    }
    private void OnApplicationFocus(bool focus) { if (focus) AppearancePreference.RefreshDevice(); }
}
