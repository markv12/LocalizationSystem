using UnityEngine;

public abstract class LanguageChangedHandler : MonoBehaviour {
    // Keep the hook for subclasses that initialize their component references in Awake.
    protected virtual void Awake() { }

    protected virtual void OnEnable() {
        Localizer.LanguageChangedEvent -= Refresh;
        Refresh();
        Localizer.LanguageChangedEvent += Refresh;
    }

    protected virtual void OnDisable() {
        Localizer.LanguageChangedEvent -= Refresh;
    }

    protected abstract void Refresh();

    protected virtual void OnDestroy() {
        Localizer.LanguageChangedEvent -= Refresh;
    }
}
