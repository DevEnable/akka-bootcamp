module Actors

open System
open System.Windows.Forms.DataVisualization.Charting
open Akka.Actor
open Akka.FSharp

let chartingActor (chart: Chart) =
    let seriesIndex = ref Map.empty  

    let addSeries (series : Series) =
        seriesIndex := (!seriesIndex).Add (series.Name, series)
        chart.Series.Add series

    (fun message -> 
        match message with
        | InitializeChart series -> 
            chart.Series.Clear ()
            series |> Map.iter (fun k v -> 
                v.Name <- k
                chart.Series.Add(v))
        | AddSeries series when
            not(String.IsNullOrEmpty series.Name) &&
            not(!seriesIndex |> Map.containsKey series.Name) ->
                addSeries series
        | _ -> ()
    )

    