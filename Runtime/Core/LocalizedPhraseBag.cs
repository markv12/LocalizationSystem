using System.Collections.Generic;

// Each consumer owns its category's selection history.
public sealed class LocalizedPhraseBag {
    private readonly string category;
    private readonly List<string> remaining = new List<string>();
    private string previous;

    public LocalizedPhraseBag(string category) => this.category = category;

    public bool TryNext(out string key) {
        if (remaining.Count == 0) {
            remaining.AddRange(Localizer.GetCategoryKeys(category));
            for (int i = remaining.Count - 1; i > 0; i--) {
                int j = UnityEngine.Random.Range(0, i + 1);
                (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
            }
            int last = remaining.Count - 1;
            if (last > 0 && remaining[last] == previous) {
                (remaining[0], remaining[last]) = (remaining[last], remaining[0]);
            }
        }
        if (remaining.Count == 0) { key = null; return false; }
        key = remaining[remaining.Count - 1];
        remaining.RemoveAt(remaining.Count - 1);
        previous = key;
        return true;
    }
}
