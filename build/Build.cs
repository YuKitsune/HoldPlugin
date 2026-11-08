using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.Git;
using Fallout.Common.IO;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Tools.GitHub;
using Fallout.Common.Tools.GitVersion;
using Fallout.Common.Tools.ILRepack;
using Octokit;
using Serilog;

[SupportedOSPlatform("Windows")]
[GitHubActions(
    "build",
    GitHubActionsImage.WindowsLatest,
    OnPushBranches = ["main"],
    OnPullRequestBranches = ["main"],
    InvokedTargets = [nameof(Compile)],
    FetchDepth = 0)]
[GitHubActions(
    "release",
    GitHubActionsImage.WindowsLatest,
    OnPushTags = ["v*"],
    InvokedTargets = [nameof(Release)],
    ImportSecrets = [nameof(GitHubToken)],
    EnableGitHubToken = true,
    FetchDepth = 0,
    WritePermissions = [GitHubActionsPermissions.Contents])]
class Build : FalloutBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>();

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    
    [Parameter]
    string ProfileName { get; }
    
    [Parameter("GitHub token for creating releases")]
    [Secret]
    readonly string GitHubToken;

    [GitRepository]
    readonly GitRepository GitRepository;

    [GitVersion]
    readonly GitVersion GitVersion;

    const string ReleasePluginName = "HoldPlugin";
    const string DebugPluginName = "HoldPlugin - Debug";
    const string PluginAssemblyFileName = "HoldPlugin.dll";

    // Harmony must stay a separate assembly. Do not merge it into the plugin assembly.
    const string HarmonyAssemblyFileName = "0Harmony.dll";

    string PluginName => Configuration == Configuration.Debug ? DebugPluginName : ReleasePluginName;

    AbsolutePath PluginProjectPath => RootDirectory / "source" / "HoldPlugin" / "HoldPlugin.csproj";
    AbsolutePath PluginBuildOutputDirectory => TemporaryDirectory / "build";
    AbsolutePath PluginZipPath => TemporaryDirectory / $"HoldPlugin.{GetSemanticVersion()}.zip";
    AbsolutePath PluginPackageDirectory => TemporaryDirectory / "package";

    // vatSys paths
    [Parameter("Path to the vatSys installation")]
    AbsolutePath VatSysPath { get; }
    AbsolutePath VatSysSetupDirectory => TemporaryDirectory / "vatsys-setup";
    AbsolutePath VatSysExePath => VatSysPath ?? VatSysSetupDirectory / "bin" / "vatSys.exe";

    Target DownloadVatSys => _ => _
        .OnlyWhenStatic(() => VatSysPath == null && !VatSysExePath.FileExists())
        .Executes(async () =>
        {
            var vatSysSetupUrl = "https://vatsys.sawbe.com/downloads/vatSysSetup.zip";
            var zipPath = TemporaryDirectory / "vatSysSetup.zip";
            var msiPath = TemporaryDirectory / "vatSysSetup.msi";

            Log.Information("Downloading vatSys from {Url}", vatSysSetupUrl);
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(vatSysSetupUrl);
            response.EnsureSuccessStatusCode();
            await using var fileStream = File.Create(zipPath);
            await response.Content.CopyToAsync(fileStream);
            fileStream.Close();

            Log.Information("Extracting vatSysSetup.zip");
            ZipFile.ExtractToDirectory(zipPath, TemporaryDirectory, overwriteFiles: true);

            Log.Information("Extracting vatSysSetup.msi");
            VatSysSetupDirectory.CreateOrCleanDirectory();

            // Use msiexec to extract the MSI contents
            var msiExtractProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "msiexec",
                Arguments = $"/a \"{msiPath}\" /qn TARGETDIR=\"{VatSysSetupDirectory}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (msiExtractProcess != null)
            {
                await msiExtractProcess.WaitForExitAsync();
                if (msiExtractProcess.ExitCode != 0)
                {
                    var error = await msiExtractProcess.StandardError.ReadToEndAsync();
                    throw new Exception($"Failed to extract MSI: {error}");
                }
            }

            if (!VatSysExePath.FileExists())
                throw new Exception($"vatSys.exe not found at {VatSysExePath}");

            Log.Information("vatSys.exe extracted to {Path}", VatSysExePath);
        });

    Target Compile => _ => _
        .DependsOn(DownloadVatSys)
        .Executes(() =>
        {
            var version = GetSemanticVersion();
            Log.Information(
                "Building version {Version} with configuration {Configuration} to {OutputDirectory}",
                version,
                Configuration,
                PluginBuildOutputDirectory);

            DotNetTasks.DotNetBuild(s => s
                .SetProjectFile(PluginProjectPath)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(PluginBuildOutputDirectory)
                .SetVersion(version)
                .SetAssemblyVersion(GitVersion?.MajorMinorPatch ?? "0.0.0")
                .SetFileVersion(GitVersion?.MajorMinorPatch ?? "0.0.0")
                .SetInformationalVersion(version)
                .SetProperty("VatSysPath", VatSysExePath.Parent.Parent));
        });

    Target Repack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var mainAssembly = PluginBuildOutputDirectory / PluginAssemblyFileName;
            var assembliesToMerge = PluginBuildOutputDirectory
                .GlobFiles("*.dll")
                .Except([mainAssembly, PluginBuildOutputDirectory / HarmonyAssemblyFileName])
                .ToArray();

            if (!mainAssembly.FileExists())
                throw new Exception($"Main assembly not found: {mainAssembly}");

            foreach (var assembly in assembliesToMerge.Where(a => !a.FileExists()))
                Log.Warning("Assembly not found (will be skipped): {Assembly}", assembly);

            var existingAssemblies = assembliesToMerge.Where(a => a.FileExists()).ToArray();
            if (existingAssemblies.Length == 0)
            {
                Log.Information("No assemblies found to repack, skipping");
                return;
            }

            var settings = new ILRepackSettings()
                .SetAssemblies([mainAssembly.ToString(), ..existingAssemblies.Select(a => a.ToString())])
                .SetInternalize(false)
                .SetParallel(true)
                .SetOutput(mainAssembly.ToString())
                .SetLib(PluginBuildOutputDirectory.ToString());  // Tell ILRepack where to find referenced assemblies

            Log.Information("Repacking {Count} assemblies into {MainAssembly}", existingAssemblies.Length, mainAssembly);
            foreach (var assembly in existingAssemblies)
                Log.Information("  - {Assembly}", assembly.Name);

            ILRepackTasks.ILRepack(settings);

            // Clean up original merged DLLs
            foreach (var assembly in existingAssemblies)
            {
                assembly.DeleteFile();
                Log.Information("Deleted {Assembly}", assembly);
            }

            Log.Information("Repack complete");
        });

    Target Uninstall => _ => _
        .Requires(() => ProfileName)
        .Executes(() =>
        {
            var pluginsDirectory = GetVatSysPluginsDirectory(ProfileName);
            AbsolutePath[] pluginDirectories =
            [
                pluginsDirectory / DebugPluginName,
                pluginsDirectory / ReleasePluginName
            ];

            foreach (var pluginDirectory in pluginDirectories)
            {
                pluginDirectory.DeleteDirectory();
                Log.Information("Plugin uninstalled from {Directory}", pluginDirectory);
            }
        });

    Target Install => _ => _
        .Requires(() => ProfileName)
        .DependsOn(Compile)
        .DependsOn(Repack)
        .DependsOn(Uninstall)
        .Executes(() =>
        {
            var pluginsDirectory = GetVatSysPluginsDirectory(ProfileName);
            Log.Information("Installing plugin to {TargetDirectory}", pluginsDirectory);

            if (!pluginsDirectory.Exists())
                pluginsDirectory.CreateDirectory();

            // Copy plugin assemblies
            var pluginDirectory = pluginsDirectory / PluginName;
            pluginDirectory.CreateOrCleanDirectory();
            foreach (var absolutePath in PluginBuildOutputDirectory.GetFiles())
            {
                absolutePath.CopyToDirectory(pluginDirectory, ExistsPolicy.MergeAndOverwrite);
            }

            Log.Information("Plugin installed to {PluginsDirectory}", pluginDirectory);
        });

    Target Package => _ => _
        .DependsOn(Compile)
        .DependsOn(Repack)
        .Requires(() => Configuration == Configuration.Release)
        .Executes(() =>
        {
            var dpiAwareFixScript = RootDirectory / "dpiawarefix.bat";
            var unblockDllsScript = RootDirectory / "unblock-dlls.bat";

            PluginPackageDirectory.CreateOrCleanDirectory();

            // Copy plugin assemblies
            foreach (var absolutePath in PluginBuildOutputDirectory.GetFiles().Where(f => f.Extension != ".pdb"))
            {
                absolutePath.CopyToDirectory(PluginPackageDirectory, ExistsPolicy.MergeAndOverwrite);
            }

            dpiAwareFixScript.CopyToDirectory(PluginPackageDirectory, ExistsPolicy.FileOverwrite);
            unblockDllsScript.CopyToDirectory(PluginPackageDirectory, ExistsPolicy.FileOverwrite);

            if (PluginZipPath.FileExists())
                PluginZipPath.DeleteFile();

            Log.Information("Packaging {OutputDirectory} to {ZipPath}", PluginPackageDirectory, PluginZipPath);
            PluginPackageDirectory.ZipTo(PluginZipPath);
        });

    Target Release => _ => _
        .DependsOn(Package)
        .Requires(() => GitHubToken)
        .Requires(() => GitRepository)
        .Requires(() => Configuration == Configuration.Release)
        .Executes(async () =>
        {
            var version = GetSemanticVersion();
            var tagName = $"v{version}";

            Log.Information("Creating GitHub release {TagName}", tagName);

            var credentials = new Credentials(GitHubToken);
            var githubClient = new GitHubClient(new ProductHeaderValue("nuke-build"))
            {
                Credentials = credentials
            };

            var repositoryOwner = GitRepository.GetGitHubOwner();
            var repositoryName = GitRepository.GetGitHubName();

            var newRelease = new NewRelease(tagName)
            {
                Name = version,
                Draft = false,
                Prerelease = false,
                GenerateReleaseNotes = true
            };

            var release = await githubClient.Repository.Release.Create(repositoryOwner, repositoryName, newRelease);
            Log.Information("Release created: {ReleaseUrl}", release.HtmlUrl);

            // Upload the zip file as an asset
            using var zipStream = File.OpenRead(PluginZipPath);
            var assetUpload = new ReleaseAssetUpload
            {
                FileName = PluginZipPath.Name,
                ContentType = "application/zip",
                RawData = zipStream
            };

            var asset = await githubClient.Repository.Release.UploadAsset(release, assetUpload);
            Log.Information("Asset uploaded: {AssetUrl}", asset.BrowserDownloadUrl);
        });

    static AbsolutePath GetVatSysPluginsDirectory(string profileName)
    {
        return GetVatSysProfilePath(profileName) / "Plugins";
    }

    static AbsolutePath GetVatSysProfilePath(string profileName)
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documentsPath, "vatSys Files", "Profiles", profileName);
    }

    private string GetSemanticVersion()
    {
        if (GitVersion is null)
        {
            return "0.0.0";
        }
        
        // For main/master branch: use major.minor.patch (e.g., "1.2.3")
        if (GitVersion.BranchName is "main" or "master")
        {
            return GitVersion.MajorMinorPatch;
        }

        // For feature branches: use major.minor.patch-feature-name (e.g., "1.2.3-feature-name")
        if (GitVersion.BranchName.StartsWith("feature/") || GitVersion.BranchName.StartsWith("features/"))
        {
            var featureName = GitVersion.BranchName
                .Replace("feature/", "")
                .Replace("features/", "")
                .Replace("/", "-")
                .Replace("_", "-");
            return $"{GitVersion.MajorMinorPatch}-{featureName}";
        }

        // For other branches (develop, hotfix, etc.): use SemVer format
        return GitVersion.SemVer;
    }
}
