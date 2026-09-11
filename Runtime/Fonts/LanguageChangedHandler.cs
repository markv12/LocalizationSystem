using UnityEngine;

public abstract class LanguageChangedHandler : MonoBehaviour {
    protected virtual void Awake() {
        Refresh();
        Localizer.LanguageChangedEvent += Refresh;
    }

    protected abstract void Refresh();

    protected virtual void OnDestroy() {
        Localizer.LanguageChangedEvent -= Refresh;
    }
}
