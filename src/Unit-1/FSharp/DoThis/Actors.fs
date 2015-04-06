module Actors

open System
open System.IO
open Akka.Actor
open Akka.FSharp
open Akka.FSharp.Spawn

[<Literal>]
let StartCommand = "start"

[<Literal>]
let ExitCommand = "exit"

[<Literal>]
let ContinueCommand = "continue"

[<Literal>]
let EmptyCommand = ""

let consoleReaderActor (validationActor: ActorRef) (mailbox: Actor<_>) message = 
    let printInstructions() =
        Console.WriteLine "Please provide the URI of a log file on disk.\n"

    let getAndValidateInput() =
        match Console.ReadLine() with
        | ExitCommand -> mailbox.Context.System.Shutdown()
        | msg -> validationActor <! msg

    match message.ToString().ToLower() with
    | StartCommand -> printInstructions()
    | _ -> ()

    getAndValidateInput()

let consoleWriterActor message = 
    let (|Even|Odd|) n = if n % 2 = 0 then Even else Odd
    
    let printInColor color message =
        Console.ForegroundColor <- color
        Console.WriteLine (message.ToString ())
        Console.ResetColor ()

    match box message with
    | :? InputResult as inputResult ->
        match inputResult with 
        | InputError(reason,_) -> printInColor ConsoleColor.Red reason
        | InputSuccess(reason) -> printInColor ConsoleColor.Green reason
    | _ -> printInColor ConsoleColor.Black (message.ToString ())

let fileValidationActor (consoleWriter : ActorRef) (tailCoordinator : ActorRef) (mailbox : Actor<_>) message =
    let (|IsFileUri|_|) path = if System.IO.File.Exists path then Some path else None
    
    match message with
    | EmptyCommand -> 
        consoleWriter <! InputError("Input was blank.  Please try again.\n", ErrorType.Null)
        mailbox.Sender () <! ContinueProcessing
    | IsFileUri _ ->
        consoleWriter <! InputSuccess(sprintf "Starting processing for %s" message)
        tailCoordinator <! StartTail(message, consoleWriter)
    | _ ->
        consoleWriter <! InputError(sprintf "%s is not an existing URI on disk." message, ErrorType.Validation)
        mailbox.Sender () <! ContinueProcessing

type TailActor(reporter, filePath) as this =
    inherit UntypedActor()

    let observer = new FileObserver(this.Self, filePath)
    do observer.Start()

    let fileStream = new FileStream(Path.GetFullPath(filePath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
    let reader = new StreamReader(fileStream, System.Text.Encoding.UTF8)
    let text = reader.ReadToEnd()
    do this.Self <! InitialRead(filePath, text)

    override this.OnReceive message =
        match message :?> FileCommand with
        | FileWrite(_) -> 
            let text = reader.ReadToEnd()

            if not <| String.IsNullOrEmpty text then reporter <! text
        | FileError(_, reason) ->
            reporter <! sprintf "Tail error: %s" reason
        | InitialRead(_, text) ->
            reporter <! text

let tailCoordinatorActor (mailbox: Actor<_>) message =
    match message with
    | StartTail(filePath,reporter) -> spawnObj mailbox.Context "tailActor" <@ (fun () -> new TailActor(reporter, filePath)) @> |> ignore
    | _ -> ()