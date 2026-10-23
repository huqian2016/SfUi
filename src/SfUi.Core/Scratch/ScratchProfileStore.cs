using System.IO.Compression;

namespace SfUi.Core;

/// <summary>
/// スクラッチ組織プロファイルの保存先（data/scratch/profiles/&lt;name&gt;）を管理する。
/// プロファイルはアプリ管理下に置き、Git 操作は行わない（ZIP でエクスポート / インポート可能）。
/// </summary>
public sealed class ScratchProfileStore
{
    public const string ProfileFileName = "profile.json";
    public const string DefinitionFileName = "definition.json";
    public const string ProjectFileName = "sfdx-project.json";
    public const string ManifestRelativePath = "manifest/package.xml";
    public const string CustomMetadataManifestRelativePath = "manifest/custom-metadata.xml";
    public const string StaticResourcesManifestRelativePath = "manifest/static-resources.xml";
    public const string ConnectedAppsManifestRelativePath = "manifest/connected-apps.xml";
    public const string ReportsManifestRelativePath = "manifest/reports.xml";
    public const string ExperienceManifestRelativePath = "manifest/experience.xml";
    public const string NamedCredentialsManifestRelativePath = "manifest/named-credentials.xml";
    public const string SeedPlanRelativePath = "data/plan.json";
    public const string PackageDirectoryRelativePath = "force-app/main/default";
    public const string ApexScriptsRelativePath = "scripts/apex";
    public const string DefaultProfileName = "default";
    private const int MaxProfiles = 200;

    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public ScratchProfileStore(AppPaths paths, AppLog log)
    {
        _paths = paths;
        _log = log;
    }

    /// <summary>data/scratch</summary>
    public string RootDirectory => Path.Combine(_paths.DataRoot, "scratch");

    /// <summary>data/scratch/profiles</summary>
    public string ProfilesDirectory => Path.Combine(RootDirectory, "profiles");

    public string GetProfileDirectory(string name) =>
        Path.Combine(ProfilesDirectory, ScratchProfileNaming.SanitizeName(name));

    /// <summary>プロファイルが使うファイル一式のパス。</summary>
    public ScratchProfileFiles GetFiles(string name)
    {
        var directory = GetProfileDirectory(name);
        return new ScratchProfileFiles(
            Path.Combine(directory, DefinitionFileName),
            Path.Combine(directory, Relative(ManifestRelativePath)),
            Path.Combine(directory, Relative(CustomMetadataManifestRelativePath)),
            Path.Combine(directory, Relative(SeedPlanRelativePath)),
            Path.Combine(directory, ProjectFileName),
            Path.Combine(directory, Relative(PackageDirectoryRelativePath)),
            Path.Combine(directory, Relative(StaticResourcesManifestRelativePath)),
            Path.Combine(directory, Relative(ConnectedAppsManifestRelativePath)),
            Path.Combine(directory, Relative(ReportsManifestRelativePath)),
            Path.Combine(directory, Relative(ExperienceManifestRelativePath)),
            Path.Combine(directory, Relative(NamedCredentialsManifestRelativePath)),
            Path.Combine(directory, Relative(ApexScriptsRelativePath)));
    }

    private static string Relative(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>保存済みプロファイル名の一覧（名前順）。</summary>
    public IReadOnlyList<string> ListProfiles()
    {
        if (!Directory.Exists(ProfilesDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(ProfilesDirectory)
            .Where(d => File.Exists(Path.Combine(d, ProfileFileName)))
            .Select(d => Path.GetFileName(d))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxProfiles)
            .ToList();
    }

    /// <summary>プロファイルを読み込む（存在しない場合は既定値 + フォルダー名）。</summary>
    public ScratchProfile Load(string name)
    {
        var sanitized = ScratchProfileNaming.SanitizeName(name);
        var profile = AtomicJsonFile.Load<ScratchProfile>(
            Path.Combine(GetProfileDirectory(sanitized), ProfileFileName), _log);
        profile.Name = sanitized;
        return profile;
    }

    /// <summary>
    /// プロファイルを読み込む。フォルダーや既定ファイルが無ければ作成する（既定プロファイルの自動作成用）。
    /// </summary>
    public ScratchProfile Ensure(string name)
    {
        var sanitized = ScratchProfileNaming.SanitizeName(name);
        var directory = GetProfileDirectory(sanitized);
        Directory.CreateDirectory(directory);

        var profilePath = Path.Combine(directory, ProfileFileName);
        if (!File.Exists(profilePath))
        {
            var profile = new ScratchProfile
            {
                Name = sanitized,
                CreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
            };
            AtomicJsonFile.Save(profilePath, profile, _log);
        }

        WriteFileIfMissing(GetFiles(sanitized).DefinitionFile, DefaultDefinitionJson);

        EnsureFiles(GetFiles(sanitized));

        return Load(sanitized);
    }

    /// <summary>プロファイルに必要な既定ファイル（マニフェスト / 定義 / SFDX プロジェクト / Apex サンプル）を作る。</summary>
    private void EnsureFiles(ScratchProfileFiles files)
    {
        WriteFileIfMissing(files.ProjectFile, DefaultSfdxProjectJson);
        WriteFileIfMissing(files.DefinitionFile, DefaultDefinitionJson);
        WriteFileIfMissing(files.ManifestFile, DefaultManifestXml);
        WriteFileIfMissing(files.CustomMetadataManifestFile, DefaultCustomMetadataManifestXml);
        WriteFileIfMissing(files.StaticResourcesManifestFile, DefaultStaticResourcesManifestXml);
        WriteFileIfMissing(files.ConnectedAppsManifestFile, DefaultConnectedAppsManifestXml);
        WriteFileIfMissing(files.ReportsManifestFile, DefaultReportsManifestXml);
        WriteFileIfMissing(files.ExperienceManifestFile, DefaultExperienceManifestXml);
        WriteFileIfMissing(files.NamedCredentialsManifestFile, DefaultNamedCredentialsManifestXml);
        WriteFileIfMissing(files.SeedPlanFile, DefaultSeedPlanJson);
        WriteFileIfMissing(Path.Combine(files.ApexScriptsDirectory, "setup.apex"), DefaultApexSample);

        Directory.CreateDirectory(files.PackageDirectory);
        Directory.CreateDirectory(files.ApexScriptsDirectory);
    }

    /// <summary>既定プロファイル（default）を用意する。失敗しても起動を止めない。</summary>
    public void EnsureDefaultProfile()
    {
        try
        {
            if (ListProfiles().Count == 0)
            {
                Ensure(DefaultProfileName);
                _log.Info("スクラッチ組織: 既定プロファイルを作成しました");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"スクラッチ組織: 既定プロファイルの作成に失敗: {ex.Message}");
        }
    }

    /// <summary>プロファイルを保存する（既定ファイルは上書きしない）。</summary>
    public void Save(ScratchProfile profile)
    {
        profile.Normalize();
        var directory = GetProfileDirectory(profile.Name);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, ProfileFileName);
        var existing = AtomicJsonFile.Load<ScratchProfile>(path, _log);
        profile.CreatedAt ??= existing.CreatedAt ?? DateTimeOffset.Now;
        profile.UpdatedAt = DateTimeOffset.Now;
        AtomicJsonFile.Save(path, profile, _log);

        EnsureFiles(GetFiles(profile.Name));
    }

    /// <summary>プロファイルを削除する（フォルダーごと）。</summary>
    public bool Delete(string name)
    {
        var directory = GetProfileDirectory(name);
        if (!Directory.Exists(directory))
        {
            return false;
        }

        Directory.Delete(directory, recursive: true);
        _log.Info($"スクラッチ組織: プロファイルを削除しました {name}");
        return true;
    }

    /// <summary>プロファイル フォルダーを ZIP にエクスポートする。</summary>
    public void Export(string name, string zipPath)
    {
        var directory = GetProfileDirectory(name);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        var parent = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(directory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        _log.Info($"スクラッチ組織: プロファイルをエクスポートしました {name} -> {zipPath}");
    }

    /// <summary>
    /// ZIP からプロファイルをインポートする。名前が衝突する場合は連番を付ける。戻り値は実際のプロファイル名。
    /// </summary>
    public string Import(string zipPath, string? preferredName = null)
    {
        var tempDirectory = Path.Combine(RootDirectory, $"import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDirectory);
            var source = FindProfileRoot(tempDirectory)
                         ?? throw new InvalidOperationException(UiText.T("Scratch_ImportNoProfile"));

            var loaded = AtomicJsonFile.Load<ScratchProfile>(Path.Combine(source, ProfileFileName), _log);
            var baseName = ScratchProfileNaming.SanitizeName(
                string.IsNullOrWhiteSpace(preferredName) ? loaded.Name : preferredName);
            var finalName = MakeUniqueName(baseName);

            var target = GetProfileDirectory(finalName);
            Directory.CreateDirectory(ProfilesDirectory);
            CopyDirectory(source, target);

            var profile = AtomicJsonFile.Load<ScratchProfile>(Path.Combine(target, ProfileFileName), _log);
            profile.Name = finalName;
            profile.UpdatedAt = DateTimeOffset.Now;
            AtomicJsonFile.Save(Path.Combine(target, ProfileFileName), profile, _log);
            Ensure(finalName);

            _log.Info($"スクラッチ組織: プロファイルをインポートしました {zipPath} -> {finalName}");
            return finalName;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private string MakeUniqueName(string baseName)
    {
        if (!Directory.Exists(GetProfileDirectory(baseName)))
        {
            return baseName;
        }

        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{baseName}-{index}";
            if (!Directory.Exists(GetProfileDirectory(candidate)))
            {
                return candidate;
            }
        }

        return $"{baseName}-{DateTime.Now:HHmmss}";
    }

    private static string? FindProfileRoot(string directory)
    {
        if (File.Exists(Path.Combine(directory, ProfileFileName)))
        {
            return directory;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (File.Exists(Path.Combine(child, ProfileFileName)))
            {
                return child;
            }
        }

        return null;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // 一時フォルダーの削除失敗は無視
        }
    }

    private void WriteFileIfMissing(string path, string content)
    {
        if (File.Exists(path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            _log.Warn($"スクラッチ組織: 既定ファイルの作成に失敗 {path}: {ex.Message}");
        }
    }

    /// <summary>既定のスクラッチ組織定義ファイル。</summary>
    public const string DefaultDefinitionJson = """
{
  "orgName": "SfUi Scratch",
  "edition": "Developer",
  "features": [
    "EnableSetPasswordInApi"
  ],
  "settings": {
    "lightningExperienceSettings": {
      "enableS1DesktopEnabled": true
    },
    "mobileSettings": {
      "enableS1EncryptedStoragePref2": false
    }
  }
}
""";

    /// <summary>既定のメタデータ マニフェスト（必要な型だけ * で取得 / デプロイする）。</summary>
    public const string DefaultManifestXml = """
<?xml version="1.0" encoding="UTF-8"?>
<Package xmlns="http://soap.sforce.com/2006/04/metadata">
    <types>
        <members>*</members>
        <name>ApexClass</name>
    </types>
    <types>
        <members>*</members>
        <name>ApexTrigger</name>
    </types>
    <types>
        <members>*</members>
        <name>ApexPage</name>
    </types>
    <types>
        <members>*</members>
        <name>CustomObject</name>
    </types>
    <types>
        <members>*</members>
        <name>CustomApplication</name>
    </types>
    <types>
        <members>*</members>
        <name>CustomTab</name>
    </types>
    <types>
        <members>*</members>
        <name>FlexiPage</name>
    </types>
    <types>
        <members>*</members>
        <name>Layout</name>
    </types>
    <types>
        <members>*</members>
        <name>PermissionSet</name>
    </types>
    <types>
        <members>*</members>
        <name>StaticResource</name>
    </types>
    <version>62.0</version>
</Package>
""";

    /// <summary>既定のカスタム メタデータ マニフェスト。</summary>
    public const string DefaultCustomMetadataManifestXml = """
<?xml version="1.0" encoding="UTF-8"?>
<Package xmlns="http://soap.sforce.com/2006/04/metadata">
    <types>
        <members>*</members>
        <name>CustomMetadata</name>
    </types>
    <version>62.0</version>
</Package>
""";

    /// <summary>既定のシードデータ プラン（objects が空の間はインポートをスキップする）。</summary>
    public const string DefaultSeedPlanJson = """
{
  "objects": []
}
""";

    /// <summary>既定の SFDX プロジェクト定義（deploy / retrieve に必須）。</summary>
    public const string DefaultSfdxProjectJson = """
{
  "packageDirectories": [
    {
      "path": "force-app",
      "default": true
    }
  ],
  "namespace": "",
  "sourceApiVersion": "62.0"
}
""";

    /// <summary>既定の Static Resource マニフェスト。</summary>
    public const string DefaultStaticResourcesManifestXml = """
<?xml version="1.0" encoding="UTF-8"?>
<Package xmlns="http://soap.sforce.com/2006/04/metadata">
    <types>
        <members>*</members>
        <name>StaticResource</name>
    </types>
    <version>62.0</version>
</Package>
""";

    /// <summary>既定の Connected App マニフェスト。</summary>
    public const string DefaultConnectedAppsManifestXml = """
<?xml version="1.0" encoding="UTF-8"?>
<Package xmlns="http://soap.sforce.com/2006/04/metadata">
    <types>
        <members>*</members>
        <name>ConnectedApp</name>
    </types>
    <version>62.0</version>
</Package>
""";

    /// <summary>既定のレポート / ダッシュボード マニフェスト。</summary>
    public const string DefaultReportsManifestXml = """
<?xml version="1.0" encoding="UTF-8"?>
<Package xmlns="http://soap.sforce.com/2006/04/metadata">
    <types>
        <members>*</members>
        <name>Report</name>
    </types>
    <types>
        <members>*</members>
        <name>ReportFolder</name>
    </types>
    <types>
        <members>*</members>
        <name>Dashboard</name>
    </types>
    <types>
        <members>*</members>
        <name>DashboardFolder</name>
    </types>
    <version>62.0</version>
</Package>
""";

    /// <summary>既定の Apex セットアップ スクリプト（コメント + 安全なデバッグ出力のみ）。</summary>
    public const string DefaultApexSample = """
// SfUi: this script runs with `sf apex run --file` when "Apex scripts" is enabled.
// Add setup statements here (create records, assign queues, ...).
System.debug('SfUi scratch org setup');
""";

    /// <summary>既定の Experience Cloud マニフェスト。</summary>
    public const string DefaultExperienceManifestXml = """
<?xml version="1.0" encoding="UTF-8"?>
<Package xmlns="http://soap.sforce.com/2006/04/metadata">
    <types>
        <members>*</members>
        <name>Network</name>
    </types>
    <types>
        <members>*</members>
        <name>CustomSite</name>
    </types>
    <types>
        <members>*</members>
        <name>ExperienceBundle</name>
    </types>
    <types>
        <members>*</members>
        <name>SiteDotCom</name>
    </types>
    <types>
        <members>*</members>
        <name>Audience</name>
    </types>
    <types>
        <members>*</members>
        <name>NavigationMenu</name>
    </types>
    <types>
        <members>*</members>
        <name>BrandingSet</name>
    </types>
    <types>
        <members>*</members>
        <name>CommunitiesSettings</name>
    </types>
    <version>62.0</version>
</Package>
""";

    /// <summary>既定の Named Credential マニフェスト。</summary>
    public const string DefaultNamedCredentialsManifestXml = """
<?xml version="1.0" encoding="UTF-8"?>
<Package xmlns="http://soap.sforce.com/2006/04/metadata">
    <types>
        <members>*</members>
        <name>NamedCredential</name>
    </types>
    <types>
        <members>*</members>
        <name>ExternalCredential</name>
    </types>
    <types>
        <members>*</members>
        <name>AuthProvider</name>
    </types>
    <version>62.0</version>
</Package>
""";
}
