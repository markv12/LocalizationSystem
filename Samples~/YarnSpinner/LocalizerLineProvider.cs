using System.Collections.Generic;
using Yarn.Unity;

/// <summary>
/// Sample Yarn Spinner line provider bridge that routes dialogue lines through Localizer.
/// Copy into your project's Assets folder if using Yarn Spinner.
/// </summary>
public class LocalizerLineProvider : LineProviderBehaviour {
    [Language]
    public string textLanguageCode = System.Globalization.CultureInfo.CurrentCulture.Name;

    public override LocalizedLine GetLocalizedLine(Yarn.Line line) {
        if (!Localizer.TryGetText(line.ID, out string lineText) || string.IsNullOrWhiteSpace(lineText)) {
            lineText = YarnProject.GetLocalization(textLanguageCode).GetLocalizedString(line.ID);
        }
        return new LocalizedLine() {
            TextID = line.ID,
            RawText = lineText,
            Substitutions = line.Substitutions,
            Metadata = YarnProject.lineMetadata.GetMetadata(line.ID),
        };
    }

    public override void PrepareForLines(IEnumerable<string> lineIDs) {
        // No-op; text lines are always loaded and available
    }

    public override bool LinesAvailable => true;

    public override string LocaleCode => textLanguageCode;
}
