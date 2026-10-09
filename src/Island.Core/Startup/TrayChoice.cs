namespace Island.Core;

/// <summary>What the tray's "open the settings file" entry does (WORK-ORDER-8 section 3): a package keeps its files in a private place that another program may not see under the usual path, so there the entry opens the settings screen instead.</summary>
public static class TrayChoice
{
    public enum SettingsFileAction
    {
        OpenTheFile,
        OpenTheScreen,
    }

    public static SettingsFileAction OpenSettingsFile(bool isPackaged) => isPackaged ? SettingsFileAction.OpenTheScreen : SettingsFileAction.OpenTheFile;
}
