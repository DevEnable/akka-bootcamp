[<AutoOpen>]
module AkkaExtensions

open System.Linq
open System.Linq.Expressions
open Microsoft.FSharp.Linq
open Akka.Actor
open Akka.FSharp.Linq

// Converts a F# code quotation that returns an actor into a LINQ expression of the same signature.
let toExpression<'Actor>(f : Quotations.Expr<(unit -> 'Actor)>) = 
    match QuotationEvaluator.ToLinqExpression f with
    | Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]) -> 
        Expression.Lambda(p, [||]) :?> System.Linq.Expressions.Expression<System.Func<'Actor>>
    | _ -> failwith "Doesn't match"

// Spawns an ActorRef using properties defined in the Quotation.
let spawnObj<'Actor when 'Actor :> ActorBase> (actorFactory : ActorRefFactory) (name : string) (f : Quotations.Expr<(unit -> 'Actor)>) : ActorRef = 
    let e = toExpression<'Actor> f
    actorFactory.ActorOf((Props.Create e), name)
    