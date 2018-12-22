#if FAKE
#r "paket:
nuget Fake.Core.Target
nuget Fake.DotNet.Cli
nuget ExcelDna.Interop
nuget ExcelDna.Integration
nuget SourceLink.Fake
nuget Fake.Tools.Git
nuget FParsec
nuget Fake.DotNet.AssemblyInfoFile
nuget Fake.DotNet.MSBuild //"
#endif
#if !FAKE
#r "netstandard" // windows
#endif
#load "./.fake/build.fsx/intellisense.fsx"
open System.IO
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.DotNet
open Fake.Core
open Fake
open Fake.Core.TargetOperators
open Fake.IO.FileSystemOperators
open Fake.Tools.Git.CommandHelper
open Fake.Tools.Git

[<RequireQualifiedAccess>]
type RepoState =
    | Changed
    | None

let versionMain = "0.1.0"
let root = __SOURCE_DIRECTORY__
let srcDir = __SOURCE_DIRECTORY__
let nugetServerRepoDir = Path.getFullName "./../../Github/Paket_NugetServer"
let nugetServerBranch = "NugetStore"
let nugetServerStoreDir = nugetServerRepoDir </> ".nuget"
Directory.ensure nugetServerStoreDir
let repoName = root |> Path.GetFileName
Trace.trace ("RepoName is " + repoName)

let slnName = repoName + ".sln"

let version =  
    let _,branchMsg,_ = runGitCommand "./" (sprintf "rev-parse --abbrev-ref HEAD")
    let branchName = branchMsg |> List.exactlyOne 
    Trace.trace ("Current branch name is " + branchName)
    let _,commitNumberMsg,_ = runGitCommand "./" (sprintf "rev-list --count %A" branchName)
    let commitNumber = commitNumberMsg |> List.exactlyOne 
    Trace.trace ("Current commit number is " + commitNumber)
    sprintf "%s-beta%s" versionMain commitNumber

Trace.trace ("Next version is " + version)

let repoState = 
    let ok,msgs,error = 
        let command = 
            [ "checkout"]
            |> Args.toWindowsCommandLine 
        runGitCommand "./" command
    match msgs with 
    | [msg] when msg.Contains "up to date" -> RepoState.None
    | _ -> RepoState.Changed

Trace.trace (sprintf "Repo state is %A" repoState)

let inline dtntSmpl arg = DotNet.Options.lift id arg


let dotnet dir command args =
    DotNet.exec 
        (fun ops -> {ops with WorkingDirectory = dir})
        command
        args
        |> ignore    


Target.create "Clean" (fun _ ->
    !! "./**/bin"
    ++ "./**/obj"
    |> Shell.cleanDirs
)


Target.create "WorkaroundPaketNuspecBug" (fun _ ->
    !! "./*/obj/**/*.nuspec"
    |> File.deleteAll
)

Target.create "CreateSln" (fun _ ->
    if not <| File.exists slnName then
        dotnet "./" "new sln" ""
        !! (srcDir + "/*/*.fsproj")
        |> Seq.iter (fun proj ->
            dotnet "./" (sprintf "sln %s add" slnName) proj    
        )
)


let pushToNugetServer() =
    Staging.stageAll nugetServerRepoDir



Target.create "PrivateGitPush" (fun _ ->
    match repoState with 
    | RepoState.Changed ->
        Staging.stageAll "./"
        Commit.exec "./" (sprintf "Bump version to %s" version)
        Branches.push "./"
    | RepoState.None -> ()        
)
 
Target.create "NugetServerPush" (fun _ ->
    match repoState with 
    | RepoState.Changed ->
        Branches.checkoutBranch nugetServerRepoDir nugetServerBranch
        Staging.stageAll nugetServerRepoDir 
        // Commit.exec nugetServerRepoDir  (sprintf "Bump %s version to %s" repoName version)
        // Branches.push nugetServerRepoDir 
    | RepoState.None -> ()        
)

Target.create "PackToNugetServer" (fun _ ->
    match repoState with 
    | RepoState.Changed ->
        DotNet.pack (fun c ->
            let versionParam = "/p:Version=" + version
            {c with 
                OutputPath = Some nugetServerStoreDir
                Common = {c.Common with CustomParams = Some versionParam}
            } |> dtntSmpl
        ) slnName
    | RepoState.None -> ()    
)


Target.create "Default" ignore
Target.create "Publish" ignore


"WorkaroundPaketNuspecBug"
    ==> "PackToNugetServer"
    ==> "PrivateGitPush"
    ==> "NugetServerPush"
    ==> "Publish"


Target.runOrDefault "Default"
