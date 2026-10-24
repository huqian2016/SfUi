using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>スクラッチ組織のプロファイル / コマンド組み立て / 実行計画 / ZIP 入出力のテスト。</summary>
[Collection("Localization")]
public class ScratchOrgTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 23, 13, 45, 59, TimeSpan.FromHours(9));

    // ---- エイリアス ----

    [Fact]
    public void ExpandAlias_ReplacesTokens()
    {
        var alias = ScratchProfileNaming.ExpandAlias("scratch-{profile}-{date}-{time}", "dev", FixedNow);

        Assert.Equal("scratch-dev-20261023-134559", alias);
    }

    [Fact]
    public void ExpandAlias_ReplacesInvalidCharacters()
    {
        var alias = ScratchProfileNaming.ExpandAlias("scratch {profile} 01!", "my profile", FixedNow);

        Assert.Equal("scratch-my-profile-01", alias);
    }

    [Fact]
    public void ExpandAlias_UsesDefaultPattern_WhenEmpty()
    {
        var alias = ScratchProfileNaming.ExpandAlias(null, "dev", FixedNow);

        Assert.Equal("scratch-dev-20261023", alias);
    }

    [Fact]
    public void SanitizeName_KeepsAllowedCharactersOnly()
    {
        Assert.Equal("a-b_c.d", ScratchProfileNaming.SanitizeName("a b_c.d"));
        Assert.Equal("default", ScratchProfileNaming.SanitizeName("   "));
        Assert.False(ScratchProfileNaming.IsValidName("bad/name"));
        Assert.True(ScratchProfileNaming.IsValidName("good-name_1"));
    }

    // ---- コマンド組み立て ----

    [Fact]
    public void BuildCreateArgs_IncludesAllOptions()
    {
        var args = ScratchOrgService.BuildCreateArgs(
            "hub", @"C:\profiles\dev\definition.json", "scratch-dev", 7, 10, setDefault: true, orgShapeId: "00Dxx0000000001");

        Assert.Equal("org", args[0]);
        Assert.Contains("--target-dev-hub", args);
        Assert.Contains("hub", args);
        Assert.Contains("--duration-days", args);
        Assert.Contains("7", args);
        Assert.Contains("--set-default", args);
        Assert.Contains("--source-org", args);
        Assert.Contains("00Dxx0000000001", args);
        Assert.Equal("--json", args[^1]);
    }

    [Fact]
    public void BuildCreateArgs_OmitsOptionalOptions()
    {
        var args = ScratchOrgService.BuildCreateArgs("hub", "def.json", "alias", 3, 5, setDefault: false);

        Assert.DoesNotContain("--set-default", args);
        Assert.DoesNotContain("--source-org", args);
    }

    [Fact]
    public void BuildDeployArgs_UsesManifestAndTarget()
    {
        var args = ScratchOrgService.BuildDeployArgs("manifest/package.xml", "scratch-dev");

        Assert.Equal(new[] { "project", "deploy", "start" }, args.Take(3));
        Assert.Contains("--manifest", args);
        Assert.Contains("manifest/package.xml", args);
        Assert.Contains("--ignore-conflicts", args);
    }

    [Fact]
    public void BuildAssignPermissionSetArgs_UsesName()
    {
        var args = ScratchOrgService.BuildAssignPermissionSetArgs("SfUi_Admin", "scratch-dev");

        Assert.Equal(new[] { "org", "assign", "permset", "--name", "SfUi_Admin", "--target-org", "scratch-dev", "--json" }, args);
    }

    // ---- 実行計画 ----

    [Fact]
    public void BuildPlan_StopsAtSelectedResourcesOnly()
    {
        var root = CreateTempRoot();
        try
        {
            var files = CreateFiles(root, includeSeed: true);
            var profile = new ScratchProfile
            {
                Name = "dev",
                UseMetadata = false,
                UseCustomMetadata = false,
                UsePermissionSets = false,
                UseSeedData = false,
                PermissionSets = new List<string> { "Ignored" },
            };

            var steps = ScratchOrgService.BuildPlan(profile, "hub", "scratch-dev", files);

            Assert.Equal(new[] { "create", "verify" }, steps.Select(s => s.Kind));
            Assert.All(steps, step => Assert.StartsWith("sf ", step.CommandLine));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildPlan_AddsSelectedResourcesInOrder()
    {
        var root = CreateTempRoot();
        try
        {
            var files = CreateFiles(root, includeSeed: true);
            var profile = new ScratchProfile
            {
                Name = "dev",
                UseMetadata = true,
                UseCustomMetadata = true,
                UsePermissionSets = true,
                UseSeedData = true,
                PermissionSets = new List<string> { "SfUi_Admin", "SfUi_ReadOnly" },
            };

            var steps = ScratchOrgService.BuildPlan(profile, "hub", "scratch-dev", files);

            Assert.Equal(
                new[] { "create", "metadata", "custom-metadata", "permset", "permset", "seed", "verify" },
                steps.Select(s => s.Kind));
            Assert.Contains(steps, s => s.Arguments.Contains("SfUi_Admin"));
            Assert.Contains(steps, s => s.Arguments.Contains("SfUi_ReadOnly"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildPlan_SkipsSeed_WhenObjectsAreEmpty()
    {
        var root = CreateTempRoot();
        try
        {
            var files = CreateFiles(root, includeSeed: false);
            var profile = new ScratchProfile { Name = "dev", UseMetadata = false, UseCustomMetadata = false, UsePermissionSets = false };

            var steps = ScratchOrgService.BuildPlan(profile, "hub", "scratch-dev", files);

            Assert.DoesNotContain(steps, s => s.Kind == "seed");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void HasSeedObjects_HandlesMissingAndBrokenFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "plan.json");
            Assert.False(ScratchOrgService.HasSeedObjects(path));

            File.WriteAllText(path, "{ \"objects\": [] }");
            Assert.False(ScratchOrgService.HasSeedObjects(path));

            File.WriteAllText(path, "not json");
            Assert.False(ScratchOrgService.HasSeedObjects(path));

            File.WriteAllText(path, "{ \"objects\": [ { \"sobject\": \"Account\" } ] }");
            Assert.True(ScratchOrgService.HasSeedObjects(path));
        }
        finally
        {
            TryDelete(root);
        }
    }

    // ---- プロファイル ストア ----

    [Fact]
    public void Store_Ensure_CreatesDefaultFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);
            var profile = store.Ensure("dev");
            var files = store.GetFiles("dev");

            Assert.Equal("dev", profile.Name);
            Assert.True(File.Exists(Path.Combine(store.GetProfileDirectory("dev"), ScratchProfileStore.ProfileFileName)));
            Assert.True(File.Exists(files.DefinitionFile));
            Assert.True(File.Exists(files.ManifestFile));
            Assert.True(File.Exists(files.CustomMetadataManifestFile));
            Assert.True(File.Exists(files.SeedPlanFile));
            Assert.True(File.Exists(files.ProjectFile));
            Assert.True(Directory.Exists(files.PackageDirectory));
            Assert.True(File.Exists(files.StaticResourcesManifestFile));
            Assert.True(File.Exists(files.ConnectedAppsManifestFile));
            Assert.True(File.Exists(files.ReportsManifestFile));
            Assert.True(File.Exists(files.ExperienceManifestFile));
            Assert.True(File.Exists(files.NamedCredentialsManifestFile));
            Assert.True(File.Exists(Path.Combine(files.ApexScriptsDirectory, "setup.apex")));
            Assert.Contains("CustomMetadata", File.ReadAllText(files.CustomMetadataManifestFile));
            Assert.Contains("packageDirectories", File.ReadAllText(files.ProjectFile));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Store_SaveAndLoad_RoundTripsValues()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);
            store.Ensure("dev");
            var profile = store.Load("dev");
            profile.DevHub = "hub@example.com";
            profile.DurationDays = 30;
            profile.WaitMinutes = 20;
            profile.AliasPattern = "s-{profile}";
            profile.SetDefault = true;
            profile.SnapshotName = "Nightly";
            profile.UseMetadata = false;
            profile.UseStaticResources = true;
            profile.UseApexScripts = true;
            profile.PermissionSets = new List<string> { "A", "B", "A" };
            store.Save(profile);

            var loaded = store.Load("dev");

            Assert.Equal("hub@example.com", loaded.DevHub);
            Assert.Equal(30, loaded.DurationDays);
            Assert.Equal(20, loaded.WaitMinutes);
            Assert.Equal("s-{profile}", loaded.AliasPattern);
            Assert.True(loaded.SetDefault);
            Assert.Equal("Nightly", loaded.SnapshotName);
            Assert.False(loaded.UseMetadata);
            Assert.True(loaded.UseStaticResources);
            Assert.True(loaded.UseApexScripts);
            Assert.Equal(new[] { "A", "B" }, loaded.PermissionSets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Store_Save_ClampsOutOfRangeValues()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);
            var profile = store.Ensure("dev");
            profile.DurationDays = 99;
            profile.WaitMinutes = 0;
            store.Save(profile);

            var loaded = store.Load("dev");

            Assert.Equal(ScratchProfile.MaxDurationDays, loaded.DurationDays);
            Assert.Equal(ScratchProfile.MinWaitMinutes, loaded.WaitMinutes);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Store_ExportAndImport_RoundTripsProfile()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);
            store.Ensure("dev");
            var profile = store.Load("dev");
            profile.DevHub = "hub@example.com";
            profile.PermissionSets = new List<string> { "SfUi_Admin" };
            store.Save(profile);

            var zip = Path.Combine(root, "dev.zip");
            store.Export("dev", zip);
            Assert.True(File.Exists(zip));

            var imported = store.Import(zip, "copy");

            Assert.Equal("copy", imported);
            Assert.Contains("copy", store.ListProfiles());
            Assert.Equal("hub@example.com", store.Load("copy").DevHub);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Store_Import_AvoidsNameCollision()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);
            store.Ensure("dev");
            var zip = Path.Combine(root, "dev.zip");
            store.Export("dev", zip);

            var first = store.Import(zip, "dev");
            var second = store.Import(zip, "dev");

            Assert.Equal("dev-2", first);
            Assert.Equal("dev-3", second);
        }
        finally
        {
            TryDelete(root);
        }
    }

    // ---- 履歴 ----

    [Fact]
    public void History_Add_KeepsNewestFirst()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = AppPaths.Resolve(root);
            var store = new ScratchRunHistoryStore(paths, new AppLog(paths));
            var now = DateTimeOffset.Now;

            store.Add(new ScratchRunRecord { StartedAt = now.AddMinutes(-10), Alias = "old", Success = true });
            store.Add(new ScratchRunRecord { StartedAt = now, Alias = "new", Success = false });

            var runs = store.Load();

            Assert.Equal(2, runs.Count);
            Assert.Equal("new", runs[0].Alias);
            Assert.False(runs[0].Success);
            Assert.Equal(UiText.T("Scratch_ResultNg"), runs[0].ResultText);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void History_Add_TrimsToLimit()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = AppPaths.Resolve(root);
            var store = new ScratchRunHistoryStore(paths, new AppLog(paths));
            var now = DateTimeOffset.Now;
            for (var index = 0; index < ScratchRunHistoryStore.MaxRuns + 5; index++)
            {
                store.Add(new ScratchRunRecord { StartedAt = now.AddMinutes(index), Alias = $"a{index}", Success = true });
            }

            var runs = store.Load();

            Assert.Equal(ScratchRunHistoryStore.MaxRuns, runs.Count);
            Assert.Equal($"a{ScratchRunHistoryStore.MaxRuns + 4}", runs[0].Alias);
        }
        finally
        {
            TryDelete(root);
        }
    }

    // ---- エラー抽出 ----

    [Fact]
    public void ExtractError_PrefersJsonMessage()
    {
        var result = new SfCliResult(
            new[] { "org", "create", "scratch" },
            1,
            "{\"status\":1,\"message\":\"The signup request failed because this organization has reached its active scratch org limit\"}",
            string.Empty,
            false,
            TimeSpan.FromSeconds(3));

        var error = ScratchOrgService.ExtractError(result);

        Assert.Contains("scratch org limit", error);
    }

    [Fact]
    public void ExtractError_FallsBackToLastStderrLine()
    {
        var result = new SfCliResult(
            new[] { "project", "deploy", "start" },
            1,
            string.Empty,
            "Warning: something\nError: deploy failed",
            false,
            TimeSpan.FromSeconds(3));

        Assert.Equal("Error: deploy failed", ScratchOrgService.ExtractError(result));
    }

    // ---- 追加資源（P2） ----

    [Fact]
    public void BuildPlan_AddsAdditionalResourceSteps()
    {
        var root = CreateTempRoot();
        try
        {
            var files = CreateFiles(root, includeSeed: false);
            var profile = new ScratchProfile
            {
                Name = "dev",
                UseMetadata = true,
                UseCustomMetadata = false,
                UsePermissionSets = false,
                UseSeedData = false,
                UseStaticResources = true,
                UseConnectedApps = true,
                UseReports = true,
            };

            var steps = ScratchOrgService.BuildPlan(profile, "hub", "scratch-dev", files);

            Assert.Equal(
                new[] { "create", "metadata", "static-resources", "connected-apps", "reports", "verify" },
                steps.Select(s => s.Kind));
            // deploy はプロファイル フォルダーを cwd にして実行する
            Assert.Equal(Path.GetDirectoryName(files.ProjectFile), steps[1].WorkingDirectory);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildPlan_AddsApexScriptsInNameOrder()
    {
        var root = CreateTempRoot();
        try
        {
            var files = CreateFiles(root, includeSeed: false);
            File.WriteAllText(Path.Combine(files.ApexScriptsDirectory, "b.apex"), "System.debug('b');");
            File.WriteAllText(Path.Combine(files.ApexScriptsDirectory, "a.apex"), "System.debug('a');");
            var profile = new ScratchProfile
            {
                Name = "dev",
                UseMetadata = false,
                UseCustomMetadata = false,
                UsePermissionSets = false,
                UseSeedData = false,
                UseApexScripts = true,
            };

            var steps = ScratchOrgService.BuildPlan(profile, "hub", "scratch-dev", files);
            var apexSteps = steps.Where(s => s.Kind == "apex").ToList();

            Assert.Equal(2, apexSteps.Count);
            Assert.EndsWith("a.apex", apexSteps[0].Arguments[3]);
            Assert.EndsWith("b.apex", apexSteps[1].Arguments[3]);
            Assert.Equal(new[] { "create", "apex", "apex", "verify" }, steps.Select(s => s.Kind));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildPlan_SkipsDeploySteps_WhenThereAreNoSources()
    {
        var root = CreateTempRoot();
        try
        {
            var files = CreateFiles(root, includeSeed: false);
            Directory.Delete(files.PackageDirectory, recursive: true);
            var profile = new ScratchProfile
            {
                Name = "dev",
                UseMetadata = true,
                UseCustomMetadata = true,
                UseStaticResources = true,
                UsePermissionSets = false,
                UseSeedData = false,
            };

            var steps = ScratchOrgService.BuildPlan(profile, "hub", "scratch-dev", files);

            Assert.Equal(new[] { "create", "verify" }, steps.Select(s => s.Kind));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void HasSourceFiles_And_ListApexScripts_HandleMissingFolders()
    {
        var root = CreateTempRoot();
        try
        {
            Assert.False(ScratchOrgService.HasSourceFiles(Path.Combine(root, "missing")));
            Assert.Empty(ScratchOrgService.ListApexScripts(Path.Combine(root, "missing")));

            var files = CreateFiles(root, includeSeed: false);
            Assert.True(ScratchOrgService.HasSourceFiles(files.PackageDirectory));
            Assert.Empty(ScratchOrgService.ListApexScripts(files.ApexScriptsDirectory));
        }
        finally
        {
            TryDelete(root);
        }
    }

    // ---- 取り込み（P2） ----

    [Fact]
    public void BuildRetrieveArgs_UsesOutputDirectory()
    {
        var args = ScratchResourceService.BuildRetrieveArgs("p.xml", "acc", "out");

        Assert.Equal(new[] { "project", "retrieve", "start" }, args.Take(3));
        Assert.Contains("--output-dir", args);
        Assert.Contains("out", args);
        Assert.Equal("--json", args[^1]);
    }

    [Fact]
    public void RetrieveCompare_DetectsAddedChangedAndSame()
    {
        var root = CreateTempRoot();
        try
        {
            var retrieved = Path.Combine(root, "retrieved");
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(Path.Combine(retrieved, "classes"));
            Directory.CreateDirectory(Path.Combine(target, "classes"));
            File.WriteAllText(Path.Combine(retrieved, "package.xml"), "<Package/>");
            File.WriteAllText(Path.Combine(retrieved, "classes", "Same.cls"), "same");
            File.WriteAllText(Path.Combine(target, "classes", "Same.cls"), "same");
            File.WriteAllText(Path.Combine(retrieved, "classes", "Changed.cls"), "new");
            File.WriteAllText(Path.Combine(target, "classes", "Changed.cls"), "old");
            File.WriteAllText(Path.Combine(retrieved, "classes", "Added.cls"), "added");

            var diff = ScratchResourceService.Compare(retrieved, target);

            Assert.Equal(1, diff.AddedCount);
            Assert.Equal(1, diff.ChangedCount);
            Assert.Equal(1, diff.SameCount);
            Assert.Equal(2, diff.PendingCount);
            Assert.DoesNotContain(diff.Changes, c => c.RelativePath == "package.xml");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RetrieveApply_CopiesFilesAndKeepsBackup()
    {
        var root = CreateTempRoot();
        try
        {
            var retrieved = Path.Combine(root, "retrieved");
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(Path.Combine(retrieved, "classes"));
            Directory.CreateDirectory(Path.Combine(target, "classes"));
            File.WriteAllText(Path.Combine(retrieved, "classes", "Changed.cls"), "new");
            File.WriteAllText(Path.Combine(target, "classes", "Changed.cls"), "old");
            File.WriteAllText(Path.Combine(retrieved, "classes", "Added.cls"), "added");

            var diff = ScratchResourceService.Compare(retrieved, target);
            var applied = ScratchResourceService.Apply(diff, target);

            Assert.Equal(2, applied);
            Assert.Equal("new", File.ReadAllText(Path.Combine(target, "classes", "Changed.cls")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "classes", "Changed.cls.bak")));
            Assert.Equal("added", File.ReadAllText(Path.Combine(target, "classes", "Added.cls")));
            Assert.Equal(0, ScratchResourceService.Compare(retrieved, target).PendingCount);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildCreateArgs_IncludesSnapshot()
    {
        var args = ScratchOrgService.BuildCreateArgs("hub", "def.json", "alias", 7, 10, false, null, "Nightly");

        Assert.Contains("--snapshot", args);
        Assert.Contains("Nightly", args);
        Assert.DoesNotContain("--source-org", args);
    }

    [Fact]
    public void Profile_Validate_RejectsShapeAndSnapshotTogether()
    {
        var profile = new ScratchProfile
        {
            Name = "dev",
            OrgShapeId = "00Dxx0000000001",
            SnapshotName = "Nightly",
        };

        var errors = profile.Validate();

        Assert.Contains(UiText.T("Scratch_InvalidShapeAndSnapshot"), errors);
    }

    [Fact]
    public void BuildPlan_AddsExperienceAndNamedCredentialSteps()
    {
        var root = CreateTempRoot();
        try
        {
            var files = CreateFiles(root, includeSeed: false);
            var profile = new ScratchProfile
            {
                Name = "dev",
                UseMetadata = false,
                UseCustomMetadata = false,
                UsePermissionSets = false,
                UseSeedData = false,
                UseExperienceCloud = true,
                UseNamedCredentials = true,
            };

            var steps = ScratchOrgService.BuildPlan(profile, "hub", "scratch-dev", files);

            Assert.Equal(
                new[] { "create", "experience", "named-credentials", "verify" },
                steps.Select(s => s.Kind));
        }
        finally
        {
            TryDelete(root);
        }
    }

    // ---- Shape / スナップショット ----

    [Fact]
    public void SignupCommands_UseExpectedArguments()
    {
        Assert.Equal(new[] { "org", "list", "shape", "--json" }, ScratchSignupService.BuildListShapesArgs());
        Assert.Equal(
            new[] { "org", "create", "shape", "--target-org", "src", "--json" },
            ScratchSignupService.BuildCreateShapeArgs("src"));
        Assert.Equal(
            new[] { "org", "delete", "shape", "--target-org", "src", "--no-prompt", "--json" },
            ScratchSignupService.BuildDeleteShapeArgs("src"));
        Assert.Equal(
            new[] { "org", "list", "snapshot", "--target-dev-hub", "hub", "--json" },
            ScratchSignupService.BuildListSnapshotsArgs("hub"));

        var createSnapshot = ScratchSignupService.BuildCreateSnapshotArgs("hub", "src", "Nightly", "nightly build");
        Assert.Contains("--source-org", createSnapshot);
        Assert.Contains("--name", createSnapshot);
        Assert.Contains("Nightly", createSnapshot);
        Assert.Contains("--description", createSnapshot);
        Assert.Equal("--json", createSnapshot[^1]);

        var deleteSnapshot = ScratchSignupService.BuildDeleteSnapshotArgs("hub", "Nightly");
        Assert.Contains("--snapshot", deleteSnapshot);
        Assert.Contains("--no-prompt", deleteSnapshot);
    }

    [Fact]
    public void ParseShapes_HandlesShapeListJson()
    {
        const string json = """
            {"status":0,"result":[{"Id":"00Dxx0000000001","Name":"CorpShape","Status":"Active","CreatedDate":"2026-10-01T00:00:00.000+0000"}]}
            """;

        var shapes = ScratchSignupService.ParseShapes(json);

        var shape = Assert.Single(shapes);
        Assert.Equal("00Dxx0000000001", shape.Id);
        Assert.Equal("CorpShape", shape.Name);
        Assert.Equal("Active", shape.Status);
        Assert.Contains("CorpShape", shape.Display);
    }

    [Fact]
    public void ParseSnapshots_HandlesSnapshotListJson()
    {
        const string json = """
            {"status":0,"result":[{"Id":"0Oo000000000001","SnapshotName":"Nightly","Status":"Completed","ExpirationDate":"2026-11-01","CreatedDate":"2026-10-01"}]}
            """;

        var snapshots = ScratchSignupService.ParseSnapshots(json);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("Nightly", snapshot.Name);
        Assert.Equal("Completed", snapshot.Status);
        Assert.Equal("2026-11-01", snapshot.ExpirationDate);
    }

    [Fact]
    public void ParseSignup_HandlesEmptyAndBrokenJson()
    {
        Assert.Empty(ScratchSignupService.ParseShapes(string.Empty));
        Assert.Empty(ScratchSignupService.ParseSnapshots("not json"));
        Assert.Empty(ScratchSignupService.ParseShapes("{\"status\":0,\"result\":[]}"));
    }

    // ---- 定義ファイル / プリセット ----

    [Fact]
    public void DefinitionEditor_AddsExperienceSettingsAndKeepsExistingValues()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "definition.json");
            File.WriteAllText(path, """
                {
                  "orgName": "Keep Me",
                  "edition": "Developer",
                  "features": ["EnableSetPasswordInApi"],
                  "settings": { "lightningExperienceSettings": { "enableS1DesktopEnabled": true } }
                }
                """);

            Assert.False(ScratchDefinitionEditor.HasExperienceSettings(path));
            var changed = ScratchDefinitionEditor.AddExperienceSettings(path);
            Assert.True(changed);
            Assert.True(ScratchDefinitionEditor.HasExperienceSettings(path));

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var rootElement = document.RootElement;
            Assert.Equal("Keep Me", rootElement.GetProperty("orgName").GetString());
            Assert.Contains("Communities", rootElement.GetProperty("features").EnumerateArray().Select(e => e.GetString()));
            Assert.True(rootElement.GetProperty("settings").GetProperty("communitiesSettings")
                .GetProperty(ScratchDefinitionEditor.CommunitiesNetworksKey).GetBoolean());
            Assert.True(rootElement.GetProperty("settings").TryGetProperty("lightningExperienceSettings", out _));

            Assert.False(ScratchDefinitionEditor.AddExperienceSettings(path));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DefinitionEditor_MigratesLegacyEnableNetworksKey()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "definition.json");
            File.WriteAllText(path, """
                {
                  "features": ["Communities", "SiteDotCom"],
                  "settings": { "communitiesSettings": { "enableNetworks": true } }
                }
                """);

            // 旧キー（enableNetworks）はデプロイできないので「未設定」として扱う
            Assert.False(ScratchDefinitionEditor.HasExperienceSettings(path));
            Assert.True(ScratchDefinitionEditor.AddExperienceSettings(path));
            Assert.True(ScratchDefinitionEditor.HasExperienceSettings(path));

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var communities = document.RootElement.GetProperty("settings").GetProperty("communitiesSettings");
            Assert.False(communities.TryGetProperty(ScratchDefinitionEditor.LegacyCommunitiesNetworksKey, out _));
            Assert.True(communities.GetProperty(ScratchDefinitionEditor.CommunitiesNetworksKey).GetBoolean());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void NamedCredentialPresets_WriteValidSkeleton()
    {
        var root = CreateTempRoot();
        try
        {
            var path = ScratchNamedCredentialPresets.Write(
                root, "SapApi", ScratchNamedCredentialPreset.Sap);

            Assert.NotNull(path);
            var xml = File.ReadAllText(path!);
            Assert.Contains("<NamedCredential", xml);
            Assert.Contains("<protocol>NoAuthentication</protocol>", xml);
            Assert.Contains("your-sap-host", xml);
            Assert.DoesNotContain("password", xml, StringComparison.OrdinalIgnoreCase);

            Assert.Null(ScratchNamedCredentialPresets.Write(root, "SapApi", ScratchNamedCredentialPreset.Sap));
            Assert.False(ScratchNamedCredentialPresets.IsValidApiName("1bad"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DescribeKnownError_MapsDevHubAndSnapshotErrors()
    {
        var devHub = ScratchOrgService.DescribeKnownError(
            "NotADevHubError", "Parsing --target-dev-hub The specified org x is not a Dev Hub.");
        Assert.Equal(UiText.T("Scratch_ErrNotADevHub"), devHub);

        var snapshot = ScratchOrgService.DescribeKnownError(
            "orgSnapshot", "Org snapshots don\u2019t support one or more options you specified: features, edition");
        Assert.Contains("features", snapshot);
        Assert.Contains("edition", snapshot);
        Assert.Equal("plain", ScratchOrgService.DescribeKnownError("Other", "plain"));

        var notEnabled = ScratchOrgService.DescribeKnownError(
            "INVALID_TYPE", "Scratch Org Snapshots isn't enabled for your Dev Hub.");
        Assert.Equal(UiText.T("Scratch_ErrSnapshotNotEnabled"), notEnabled);

        var settingsFailed = ScratchOrgService.DescribeKnownError(
            "ProblemDeployingSettings",
            "A scratch org was created with username test-abc@example.com, but the settings failed to deploy due to: enableNetworks");
        Assert.Contains("test-abc@example.com", settingsFailed);
    }

    [Fact]
    public void SnapshotDefinition_StripsUnsupportedKeysOnly()
    {
        var root = CreateTempRoot();
        try
        {
            var source = Path.Combine(root, "definition.json");
            File.WriteAllText(source, """
                {
                  "orgName": "Keep Me",
                  "edition": "Developer",
                  "features": ["Communities"],
                  "settings": { "lightningExperienceSettings": { "enableS1DesktopEnabled": true } }
                }
                """);

            var unsupported = ScratchDefinitionEditor.ListSnapshotUnsupportedKeys(source);
            Assert.Contains("edition", unsupported);
            Assert.Contains("features", unsupported);
            Assert.DoesNotContain("settings", unsupported);

            var destination = ScratchDefinitionEditor.SnapshotDefinitionPath(source);
            Assert.Equal(Path.Combine(root, "definition.snapshot.json"), destination);

            var removed = ScratchDefinitionEditor.CreateSnapshotSafeDefinition(source, destination);

            Assert.Equal(2, removed.Count);
            Assert.Contains("features", removed);
            Assert.Contains("edition", removed);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(destination));
            Assert.False(document.RootElement.TryGetProperty("edition", out _));
            Assert.False(document.RootElement.TryGetProperty("features", out _));
            Assert.Equal("Keep Me", document.RootElement.GetProperty("orgName").GetString());
            Assert.True(document.RootElement.GetProperty("settings")
                .TryGetProperty("lightningExperienceSettings", out _));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildPlan_UsesSnapshotDefinitionFile()
    {
        var root = CreateTempRoot();
        try
        {
            var files = CreateFiles(root, includeSeed: false);
            var profile = new ScratchProfile
            {
                Name = "dev",
                UseMetadata = false,
                UseCustomMetadata = false,
                UsePermissionSets = false,
                UseSeedData = false,
                SnapshotName = "Nightly",
            };
            var snapshotDefinition = Path.Combine(root, "definition.snapshot.json");

            var steps = ScratchOrgService.BuildPlan(profile, "hub", "scratch-dev", files, snapshotDefinition);

            Assert.Contains(snapshotDefinition, steps[0].CommandLine);
            Assert.Contains("--snapshot", steps[0].CommandLine);
            Assert.DoesNotContain(files.DefinitionFile, steps[0].CommandLine);
        }
        finally
        {
            TryDelete(root);
        }
    }

    // ---- ヘルパー ----

    private static ScratchProfileStore CreateStore(string root)
    {
        var paths = AppPaths.Resolve(root);
        return new ScratchProfileStore(paths, new AppLog(paths));
    }

    private static ScratchProfileFiles CreateFiles(string root, bool includeSeed)
    {
        var definition = Path.Combine(root, "definition.json");
        var manifest = Path.Combine(root, "package.xml");
        var customMetadata = Path.Combine(root, "custom-metadata.xml");
        var plan = Path.Combine(root, "plan.json");
        var project = Path.Combine(root, "sfdx-project.json");
        var package = Path.Combine(root, "force-app", "main", "default");
        var staticResources = Path.Combine(root, "static-resources.xml");
        var connectedApps = Path.Combine(root, "connected-apps.xml");
        var reports = Path.Combine(root, "reports.xml");
        var experience = Path.Combine(root, "experience.xml");
        var namedCredentials = Path.Combine(root, "named-credentials.xml");
        var apexScripts = Path.Combine(root, "scripts", "apex");

        File.WriteAllText(definition, "{}");
        File.WriteAllText(manifest, "<Package/>");
        File.WriteAllText(customMetadata, "<Package/>");
        File.WriteAllText(project, "{}");
        File.WriteAllText(staticResources, "<Package/>");
        File.WriteAllText(connectedApps, "<Package/>");
        File.WriteAllText(reports, "<Package/>");
        File.WriteAllText(experience, "<Package/>");
        File.WriteAllText(namedCredentials, "<Package/>");
        Directory.CreateDirectory(Path.Combine(package, "classes"));
        File.WriteAllText(Path.Combine(package, "classes", "Sample.cls"), "public class Sample {}");
        Directory.CreateDirectory(apexScripts);
        File.WriteAllText(
            plan,
            includeSeed
                ? "{ \"objects\": [ { \"sobject\": \"Account\", \"saveRefs\": true, \"resolveRefs\": true } ] }"
                : "{ \"objects\": [] }");
        return new ScratchProfileFiles(
            definition, manifest, customMetadata, plan, project, package,
            staticResources, connectedApps, reports, experience, namedCredentials, apexScripts);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "sfui-scratch-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // テスト後の削除失敗は無視
        }
    }
}
