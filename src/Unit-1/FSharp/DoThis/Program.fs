﻿open System
open Akka.FSharp
open Akka.FSharp.Spawn
open Akka.Actor

[<EntryPoint>]
let main argv = 
    // initialize an actor system
    let myActorSystem = System.create "MyActorSystem" (Configuration.load ())

    // make your first actors using the 'spawn' function

    let consoleWriterActor = 
        spawnOpt myActorSystem "consoleWriterActor" (actorOf Actors.consoleWriterActor) [SpawnOption.SupervisorStrategy(SupervisorStrategy.DefaultStrategy)]

    let validationActor =
        spawnOpt myActorSystem "validationActor" (actorOf2 (Actors.validationActor consoleWriterActor)) [SpawnOption.Deploy(Deploy.Local)]
    
    let consoleReaderActor = 
        spawnOpt myActorSystem "consoleReaderActor" (actorOf2 (Actors.consoleReaderActor validationActor)) [SpawnOption.Router(Akka.Routing.RouterConfig.NoRouter)]
    
    consoleReaderActor <! Actors.StartCommand

    myActorSystem.AwaitTermination ()
    0
