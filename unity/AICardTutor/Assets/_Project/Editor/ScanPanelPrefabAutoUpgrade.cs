using UnityEditor;
using UnityEngine;

/// <summary>
/// Rebuilds ScanPanel.prefab once per editor session when the asset is older than the builder.
///
/// Why this exists: the layout lives in the prefab, not in code. Changing
/// <see cref="ScanPanelPrefabBuilder.ConfigureTutorPanel"/> does nothing until someone runs the
/// menu, and it is far too easy to forget that after a recompile - the panel then keeps the old
/// geometry and the rows overlap.
///
/// The check is cheap and idempotent: a current prefab is 1000 units tall and carries four
/// WildColorSector slices. Anything else is upgraded through the same code path as the menu.
/// </summary>
[InitializeOnLoad]
static class ScanPanelPrefabAutoUpgrade
{
    const string PrefabPath = "Assets/_Project/Prefabs/UI/ScanPanel.prefab";
    const string SessionKey = "CardTutor.PanelUpgradeAttempted";

    const float ExpectedHeight = 1000f;

    static ScanPanelPrefabAutoUpgrade()
    {
        // Import must finish before the prefab can be loaded and written.
        EditorApplication.delayCall += TryUpgrade;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += TryUpgrade;
        };
    }

    static void TryUpgrade()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (asset == null)
            return;

        var rect = asset.GetComponent<RectTransform>();

        bool wheelPresent = asset.GetComponentInChildren<WildColorSector>(true) != null;
        bool layoutCurrent = rect != null && Mathf.Approximately(rect.sizeDelta.y, ExpectedHeight);

        if (layoutCurrent && wheelPresent)
            return;

        SessionState.SetBool(SessionKey, true);

        Debug.Log($"[ScanPanel] prefab was out of date (height " +
                  $"{(rect != null ? rect.sizeDelta.y.ToString("0") : "?")}, " +
                  $"wheel {(wheelPresent ? "yes" : "no")}). Upgrading it now.");

        ScanPanelPrefabBuilder.UpgradeWithTutor();
    }
}
