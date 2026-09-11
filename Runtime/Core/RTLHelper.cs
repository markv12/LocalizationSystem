using RTLTMPro;
using TMPro;

public static class RTLHelper {
    private static readonly FastStringBuilder _outputBuilder = new FastStringBuilder(RTLSupport.DefaultBufferSize);

    public static void SetText(TMP_Text field, string value) {
        if (field == null) return;

        if (NeedsShaping(value)) {
            field.isRightToLeftText = true;
            field.text = Shape(value);
        } else {
            field.isRightToLeftText = false;
            field.text = value;
        }
    }

    public static bool NeedsShaping(string value) => Localizer.IsRTL && !string.IsNullOrEmpty(value) && TextUtils.IsRTLInput(value);

    // Reorders and joins Arabic glyphs into their presentation forms. UI Toolkit has no
    // isRightToLeftText equivalent, so its text must be pre-shaped through here instead.
    public static string Shape(string value) {
        if (!NeedsShaping(value)) return value;
        _outputBuilder.Clear();
        RTLSupport.FixRTL(value, _outputBuilder);
        _outputBuilder.Reverse();
        return _outputBuilder.ToString();
    }
}
