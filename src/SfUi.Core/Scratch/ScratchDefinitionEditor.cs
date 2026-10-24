using System.Text.Json;
using System.Text.Json.Nodes;

namespace SfUi.Core;

/// <summary>
/// スクラッチ定義ファイル（definition.json）を安全に編集するヘルパー。
/// Experience Cloud に必要な features / settings を追加する。
/// </summary>
public static class ScratchDefinitionEditor
{
    private static readonly string[] ExperienceFeatures = { "Communities", "SiteDotCom" };

    /// <summary>
    /// スナップショットからスクラッチ組織を作るときに指定できない definition.json のキー
    /// （sf の SNAPSHOT_UNSUPPORTED_OPTIONS と一致）。
    /// </summary>
    public static readonly IReadOnlyList<string> SnapshotUnsupportedKeys = new[]
    {
        "features",
        "orgPreferences",
        "edition",
        "sourceOrg",
        "settingsPath",
        "releaseVersion",
    };

    private static readonly string SnapshotDefinitionFileName = "definition.snapshot.json";

    /// <summary>スナップショット用の定義ファイル パス（プロファイル フォルダーの definition.snapshot.json）。</summary>
    public static string SnapshotDefinitionPath(string definitionFile) =>
        Path.Combine(Path.GetDirectoryName(definitionFile) ?? ".", SnapshotDefinitionFileName);

    /// <summary>definition.json のうち、スナップショットで使えないキーを列挙する。</summary>
    public static IReadOnlyList<string> ListSnapshotUnsupportedKeys(string definitionFile)
    {
        if (!File.Exists(definitionFile))
        {
            return Array.Empty<string>();
        }

        var root = ParseDefinition(definitionFile);
        return SnapshotUnsupportedKeys
            .Where(key => root.ContainsKey(key))
            .ToList();
    }

    /// <summary>
    /// スナップショット用の定義ファイル（使えないキーを除いたもの）を書き出し、除外したキーを返す。
    /// 対象のキーが無い場合は実質的に元ファイルのコピーになる。
    /// </summary>
    public static IReadOnlyList<string> CreateSnapshotSafeDefinition(string definitionFile, string destinationFile)
    {
        if (!File.Exists(definitionFile))
        {
            throw new FileNotFoundException(definitionFile);
        }

        var root = ParseDefinition(definitionFile);
        var removed = SnapshotUnsupportedKeys.Where(key => root.Remove(key)).ToList();
        File.WriteAllText(
            destinationFile,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return removed;
    }

    private static JsonObject ParseDefinition(string definitionFile) =>
        JsonNode.Parse(File.ReadAllText(definitionFile)) as JsonObject
        ?? throw new InvalidDataException(UiText.T("Scratch_ExperienceInvalidDefinition"));

    /// <summary>Experience Cloud 用の features / settings が既に入っているか。</summary>
    public static bool HasExperienceSettings(string definitionFile)
    {
        try
        {
            if (!File.Exists(definitionFile))
            {
                return false;
            }

            var root = JsonNode.Parse(File.ReadAllText(definitionFile)) as JsonObject;
            if (root?["features"] is not JsonArray features)
            {
                return false;
            }

            var hasFeatures = ExperienceFeatures.All(feature => ContainsFeature(features, feature));
            var enableNetworks = root["settings"]?["communitiesSettings"]?["enableNetworks"];
            return hasFeatures && enableNetworks is JsonValue value
                   && value.TryGetValue<bool>(out var enabled) && enabled;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// definition.json に Experience Cloud の features / settings を追加する（既存の値は保持）。
    /// 戻り値は書き換えたかどうか。
    /// </summary>
    public static bool AddExperienceSettings(string definitionFile)
    {
        if (!File.Exists(definitionFile))
        {
            throw new FileNotFoundException(definitionFile);
        }

        var root = JsonNode.Parse(File.ReadAllText(definitionFile)) as JsonObject
                   ?? throw new InvalidDataException(UiText.T("Scratch_ExperienceInvalidDefinition"));

        JsonArray features;
        if (root["features"] is JsonArray existingFeatures)
        {
            features = existingFeatures;
        }
        else
        {
            features = new JsonArray();
            root["features"] = features;
        }

        var changed = false;
        foreach (var feature in ExperienceFeatures)
        {
            if (!ContainsFeature(features, feature))
            {
                features.Add(feature);
                changed = true;
            }
        }

        JsonObject settings;
        if (root["settings"] is JsonObject existingSettings)
        {
            settings = existingSettings;
        }
        else
        {
            settings = new JsonObject();
            root["settings"] = settings;
        }

        JsonObject communities;
        if (settings["communitiesSettings"] is JsonObject existingCommunities)
        {
            communities = existingCommunities;
        }
        else
        {
            communities = new JsonObject();
            settings["communitiesSettings"] = communities;
        }

        if (communities["enableNetworks"] is not JsonValue current
            || !current.TryGetValue<bool>(out var enabled)
            || !enabled)
        {
            communities["enableNetworks"] = true;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        File.WriteAllText(
            definitionFile,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    private static bool ContainsFeature(JsonArray features, string feature) =>
        features.Any(item => item is JsonValue value
                             && value.TryGetValue<string>(out var text)
                             && string.Equals(text, feature, StringComparison.OrdinalIgnoreCase));
}
