#r "paket:
nuget Fake.Core.Target
nuget Fake.Core.Process
nuget Fake.DotNet.Cli
nuget Fake.Core.ReleaseNotes
nuget Fake.DotNet.AssemblyInfoFile
nuget Fake.DotNet.Paket
nuget Fake.Tools.Git
nuget Fake.Core.Environment
nuget Fake.Core.UserInput
nuget Fake.IO.FileSystem
nuget Fake.DotNet.MsBuild
nuget Fake.Api.GitHub //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.Tools
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.Api

let buildDir = "./build/"

Target.create "Clean" (fun _ ->
  Shell.cleanDir buildDir
)

Target.create "Restore" (fun _ ->
    DotNet.restore id ""
)

Target.create "Build" (fun _ ->
    DotNet.build id ""
)

// --------------------------------------------------------------------------------------
// Release Targets
// --------------------------------------------------------------------------------------
Target.create "BuildRelease" (fun _ ->
    DotNet.build (fun p ->
        { p with
            Configuration = DotNet.BuildConfiguration.Release
            OutputPath = Some buildDir
            //MSBuildParams = { p.MSBuildParams with Properties = [("Version", release.NugetVersion); ("PackageReleaseNotes", String.concat "\n" release.Notes)]}
        }
    ) "Castos.Podcatcher.sln"
)

open Fake.Core.TargetOperators

// *** Define Dependencies ***
"Clean"
  ==> "Build"

"Clean"
  ==> "BuildRelease"

// *** Start Build ***
Target.runOrDefault "Build"