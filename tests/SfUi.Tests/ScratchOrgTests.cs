using SfUi.Core;
using Xunit;

namespace SfUi.Tests;

/// <summary>スクラッチ組織のプロファイル / コマンド組み立て / 実行計画 / ZIP 入出力のテスト。</summary>
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
            Assert.Contains("CustomMetadata", File.ReadAllText(files.CustomMetadataManifestFile));
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
            profile.UseMetadata = false;
            profile.PermissionSets = new List<string> { "A", "B", "A" };
            store.Save(profile);

            var loaded = store.Load("dev");

            Assert.Equal("hub@example.com", loaded.DevHub);
            Assert.Equal(30, loaded.DurationDays);
            Assert.Equal(20, loaded.WaitMinutes);
            Assert.Equal("s-{profile}", loaded.AliasPattern);
            Assert.True(loaded.SetDefault);
            Assert.False(loaded.UseMetadata);
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
        File.WriteAllText(definition, "{}");
        File.WriteAllText(manifest, "<Package/>");
        File.WriteAllText(customMetadata, "<Package/>");
        File.WriteAllText(
            plan,
            includeSeed
                ? "{ \"objects\": [ { \"sobject\": \"Account\", \"saveRefs\": true, \"resolveRefs\": true } ] }"
                : "{ \"objects\": [] }");
        return new ScratchProfileFiles(definition, manifest, customMetadata, plan);
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
