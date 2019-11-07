// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r "paket: groupref FakeBuild //"

#load "./.fake/build.fsx/intellisense.fsx"

#if !FAKE
    #r "netstandard"
#endif
open System.IO
open System
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.DotNet.Testing
open Fake.Tools

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "XPlot"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "Data visualization library for F#"

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "XPlot is a cross-platform data visualization library that supports creating charts using Google Charts and Plotly. The library provides a composable domain specific language for building charts and specifying their properties."

// List of author names (for NuGet package)
let authors = "Taha Hachana, Tomas Petricek"

// Tags for your project (for NuGet package)
let tags = "f# fsharp data visualization html5 javascript datavis google chart plotly deedle frame dataframe"

// File system information
let solutionFile  = "XPlot.sln"

// Default target configuration
let configuration = "Release"

// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/**/bin/Release/*Tests*.dll"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "fslaborg"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "XPlot"

// The url for the raw files hosted
let gitRaw = Environment.environVarOrDefault "gitRaw" "https://raw.github.com/fslaborg"

let website = "https://fslab.org/XPlot"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
//System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let release = ReleaseNotes.load "RELEASE_NOTES.md"

// Generate assembly info files with the right version & up-to-date information
Target.create "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ AssemblyInfo.Title (projectName)
          AssemblyInfo.Product project
          AssemblyInfo.Description summary
          AssemblyInfo.Version release.AssemblyVersion
          AssemblyInfo.FileVersion release.AssemblyVersion ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        ( projectPath, projectName,
          System.IO.Path.GetDirectoryName(projectPath),
          (getAssemblyInfoAttributes projectName) )

    !! "src/**/*.fsproj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (_, _, folderName, attributes) ->
        AssemblyInfoFile.createFSharp (folderName </> "AssemblyInfo.fs") attributes )
)

// Copies binaries from default VS location to expected bin folder
// But keeps a subdirectory structure for each project in the
// src folder to support multiple project outputs
Target.create "CopyBinaries" (fun _ ->
    !! "src/**/*.??proj"
    -- "src/**/*.shproj"
    |>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) </> "bin" </> configuration, "bin" </> (System.IO.Path.GetFileNameWithoutExtension f)))
    |>  Seq.iter (fun (fromDir, toDir) -> Shell.copyDir toDir fromDir (fun _ -> true))
)

// --------------------------------------------------------------------------------------
// Clean build results

let buildConfiguration = DotNet.Custom <| Environment.environVarOrDefault "configuration" configuration

Target.create "Clean" (fun _ ->
    Shell.cleanDirs ["bin"; "temp"]
)

Target.create "CleanDocs" (fun _ ->
    Shell.cleanDirs ["docs/output"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target.create "Build" (fun _ ->
    solutionFile 
    |> DotNet.build (fun p -> 
        { p with
            Configuration = buildConfiguration })
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target.create "RunTests" (fun _ ->
    !! testAssemblies
    |> NUnit.Sequential.run (fun p ->
        { p with
            DisableShadowCopy = true
            TimeOut = System.TimeSpan.FromMinutes 20.
            OutputFile = "TestResults.xml" })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "NuGet" (fun _ ->
    let releaseNotes = release.Notes |> String.toLines

    Paket.pack(fun p -> 
        { p with
            OutputPath = "bin"
            Version = release.NugetVersion
            ReleaseNotes = releaseNotes})
)

Target.create "PublishNuget" (fun _ ->
    Paket.push(fun p ->
        { p with
            WorkingDir = "bin"
            ApiKey = Environment.environVarOrDefault "NugetKey" "" })
)

// --------------------------------------------------------------------------------------
// Generate the documentation
Target.create "GenerateDocs" (fun _ ->
  let (exitCode, messages) = Fsi.exec (fun p -> { p with WorkingDirectory="docsrc/tools"; Define="RELEASE"; }) "generate.fsx" []
  if exitCode = 0 then () else 
    failwith (messages |> String.concat Environment.NewLine)
)

// --------------------------------------------------------------------------------------
// Release Scripts

Target.create "ReleaseDocs" (fun _ ->
    Git.Repository.clone "" (gitHome + "/" + gitName + ".git") "temp/gh-pages"
    Git.Branches.checkoutBranch "temp/gh-pages" "gh-pages"
    Shell.copyRecursive "docs/output" "temp/gh-pages" true |> printfn "%A"
    Git.CommandHelper.runSimpleGitCommand "temp/gh-pages" "add ." |> printfn "%s"
    let cmd = sprintf """commit -a -m "Update generated documentation for version %s""" release.NugetVersion
    Git.CommandHelper.runSimpleGitCommand "temp/gh-pages" cmd |> printfn "%s"
    Git.Branches.push "temp/gh-pages"
)

Target.create "Release" (fun _ ->
    Git.Staging.stageAll ""
    Git.Commit.exec "" (sprintf "Bump version to %s" release.NugetVersion)
    Git.Branches.push ""

    Git.Branches.tag "" release.NugetVersion
    Git.Branches.pushTag "" "origin" release.NugetVersion

    // release on github
    // createClient (getBuildParamOrDefault "github-user" "") (getBuildParamOrDefault "github-pw" "")
    // |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
    // // TODO: |> uploadFile "PATH_TO_FILE"
    // |> releaseDraft
    // |> Async.RunSynchronously
)

Target.create "BuildPackage" ignore

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

let isLocalBuild = (BuildServer.buildServer = LocalBuild)

Target.create "All" ignore

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "CopyBinaries"
  ==> "All"

"All"
  ==> "NuGet"

"All"
  ==> "CleanDocs"
  ==> "GenerateDocs"
  ==> "ReleaseDocs"

"BuildPackage"
  ==> "PublishNuget"
  ==> "Release"

Target.runOrDefault "All"