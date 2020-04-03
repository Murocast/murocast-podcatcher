#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.DotNet.MSBuild
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.IO
open Fake.IO.Globbing.Operators //enables !! and globbing
open Fake.DotNet
open Fake.Core

let buildDir = "./build/"

Target.create "Clean" (fun _ ->
  Shell.cleanDir buildDir
)

Target.create "Build" (fun _ ->
    !! "src/**/*.fsproj"
      |> MSBuild.runRelease id buildDir "Build"
      |> Trace.logItems "AppBuild-Output: "
)

Target.create "Deploy" (fun _ ->
  Trace.log " --- Deploying app --- "
)

open Fake.Core.TargetOperators

// *** Define Dependencies ***
"Clean"
  ==> "Build"
  ==> "Deploy"

// *** Start Build ***
Target.runOrDefault "Deploy"