module Actors

open System
open Akka.Actor
open Akka.FSharp

[<Literal>]
let ExitCommand = "exit"

[<Literal>]
let StartCommand = "start"

[<Literal>]
let ContinueCommand = "continue"

[<Literal>]
let EmptyCommand = ""

let consoleReaderActor (consoleWriter: ActorRef) (mailbox: Actor<_>) message = 
    
    let (|ValidMessage|_|) (msg : string) = if msg.Length % 2 = 0 then Some msg else None
        
    let doPrintInstructions() =
        Console.WriteLine "Write whatever you want into the console!"
        Console.WriteLine "Some entries will pass validation, and some won't...\n\n"
        Console.WriteLine "Type 'exit' to quit this application at any time.\n"

    let getAndValidateInput() =
        let message = Console.ReadLine()

        match message with
        | EmptyCommand -> consoleWriter <! InputError("No input received.", NullInput)
        | ExitCommand -> mailbox.Context.System.Shutdown()
        | ValidMessage _ -> 
            consoleWriter <! InputSuccess "Thank you mesage was valid"
            mailbox.Self <! ContinueProcessing
        | _ -> 
            consoleWriter <! InputError("Invalid: input had odd number of characters.", Validation)
            mailbox.Self <! ContinueProcessing

    match box message with
    | :? string as command ->
        match command with
        | StartCommand -> doPrintInstructions()
        | _ -> ()
    | :? InputResult as result -> 
        match result with
        | InputError(_, _) as error -> consoleWriter <! error
        | _ -> ()
    | _  -> ()
    
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
        | InputError(msg, _) ->
            printInColor ConsoleColor.Red msg
        | InputSuccess msg ->
            printInColor ConsoleColor.Green msg
    | _ -> 
        message.ToString() 
        |> printInColor ConsoleColor.DarkYellow 
