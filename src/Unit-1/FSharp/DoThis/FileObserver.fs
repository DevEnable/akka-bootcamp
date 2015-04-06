[<AutoOpen>]
module WinTail

open System
open System.IO
open Akka.Actor
open Akka.FSharp

type FileObserver(tailActor: ActorRef, absoluteFilePath: string) =
    let fileDir = Path.GetDirectoryName absoluteFilePath
    let fileNameOnly = Path.GetFileName absoluteFilePath
    let mutable watcher = null : FileSystemWatcher

    member x.Start() =
        Environment.SetEnvironmentVariable("MONO_MANAGED_WATCHER", "enabled"); // uncomment this line if you're running on Mono!

        watcher <- new FileSystemWatcher(fileDir, fileNameOnly)
        watcher.NotifyFilter <- NotifyFilters.FileName ||| NotifyFilters.LastWrite
        watcher.Changed.Add (fun e -> if e.ChangeType = WatcherChangeTypes.Changed then tailActor <! FileWrite(e.Name))
        watcher.Error.Add(fun e -> tailActor <! FileError(fileNameOnly, e.GetException().Message))
        watcher.EnableRaisingEvents <- true