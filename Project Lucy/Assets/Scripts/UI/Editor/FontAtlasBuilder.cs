using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rebuilds the atlas of the Jersey10 font asset so every character used by the game's Portuguese texts lives in
/// the font itself. The original 1024x1024 atlas, sampled at 200pt, filled up after ~90 glyphs: characters that no
/// longer fit (Õ, Ú, Í...) silently fell back to TMP's default LiberationSans, which is why they rendered bigger
/// than the letters around them and broke the menu texts.
/// The rebuild grows the atlas to 2048x2048, turns on extra atlas pages as a safety net, pre-bakes the ASCII and
/// accented Latin characters and keeps them in builds (the asset used to clear its dynamic data on build).
/// Safe to run again: it always starts from an empty atlas.
/// </summary>
public static class FontAtlasBuilder
{
    private const string FontPath = "Assets/Renan/Fonts/Jersey10-Regular SDF 1.asset";
    private const int AtlasSize = 2048;

    private const string AccentedCharacters =
        "ÀÁÂÃÄÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜàáâãäçèéêëìíîïñòóôõöùúûü";
    private const string ExtraCharacters = "…–—«»ºª°¡¿";

    [MenuItem("Tools/Lucy/Rebuild Font Atlas")]
    public static void Rebuild()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Fonte] Fonte nao encontrada em {FontPath}.");
            return;
        }

        SerializedObject serialized = new(font);
        serialized.FindProperty("m_AtlasWidth").intValue = AtlasSize;
        serialized.FindProperty("m_AtlasHeight").intValue = AtlasSize;
        serialized.FindProperty("m_IsMultiAtlasTexturesEnabled").boolValue = true;
        serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
        serialized.FindProperty("m_CreationSettings.atlasWidth").intValue = AtlasSize;
        serialized.FindProperty("m_CreationSettings.atlasHeight").intValue = AtlasSize;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.ClearFontAssetData(setAtlasSizeToZero: true);

        string ascii = new(Enumerable.Range(32, 95).Select(c => (char)c).ToArray());
        bool allAdded = font.TryAddCharacters(ascii + AccentedCharacters + ExtraCharacters, out string missing);

        if (font.material != null)
        {
            font.material.SetFloat(ShaderUtilities.ID_TextureWidth, font.atlasTexture.width);
            font.material.SetFloat(ShaderUtilities.ID_TextureHeight, font.atlasTexture.height);
            EditorUtility.SetDirty(font.material);
        }

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, font);

        string summary = $"[Fonte] Atlas de {FontPath} reconstruido: {font.characterTable.Count} caracteres, " +
                         $"{font.atlasTextures.Length} pagina(s) de {font.atlasTexture.width}x{font.atlasTexture.height}.";
        if (allAdded) Debug.Log(summary);
        else Debug.LogWarning($"{summary} Caracteres que a fonte Jersey10 nao tem: \"{missing}\".");
    }
}
