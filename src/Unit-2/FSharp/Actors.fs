module Actors

open System
open System.Collections.Generic
open System.Diagnostics
open System.Drawing
open System.Threading
open System.Windows.Forms
open System.Windows.Forms.DataVisualization.Charting
open Akka.Actor
open Akka.FSharp

let chartingActor (chart: Chart) (pauseButton : Button) =
    let maxPoints = 250
    let xPosCounter = ref 0.
    let seriesIndex = ref Map.empty  
   
    let addSeries (series : Series) =
        seriesIndex := (!seriesIndex).Add (series.Name, series)
        chart.Series.Add series
 
    let setChartBoundaries () =
        let allPoints = (!seriesIndex) |> Map.toList |> Seq.collect (fun (n, s) -> s.Points) |> (fun p -> HashSet<DataPoint>(p))
        if allPoints |> Seq.length > 2 then
            let yValues = allPoints |> Seq.collect (fun p -> p.YValues) |> Seq.toList
            chart.ChartAreas.[0].AxisX.Maximum <- !xPosCounter
            chart.ChartAreas.[0].AxisX.Minimum <- (!xPosCounter - float maxPoints)
            chart.ChartAreas.[0].AxisY.Maximum <- if yValues |> List.length > 0 then Math.Ceiling(yValues |> List.max) else 1.
            chart.ChartAreas.[0].AxisY.Minimum <- if yValues |> List.length > 0 then Math.Floor(yValues |> List.min) else 0.
        else
            ()

    let runningHandler message =
        match message with
        | InitializeChart series -> 
            chart.Series.Clear ()
            chart.ChartAreas.[0].AxisX.IntervalType <- DateTimeIntervalType.Number
            chart.ChartAreas.[0].AxisY.IntervalType <- DateTimeIntervalType.Number
            series |> Map.iter (fun k v -> 
                                    v.Name <- k
                                    v |> addSeries)
        | AddSeries series when not (String.IsNullOrEmpty series.Name) && not (!seriesIndex |> Map.containsKey series.Name) -> addSeries series
        | RemoveSeries seriesName when not (String.IsNullOrEmpty seriesName) && !seriesIndex |> Map.containsKey seriesName -> 
            chart.Series.Remove (!seriesIndex).[seriesName] |> ignore
            seriesIndex := (!seriesIndex).Remove seriesName
        | Metric(seriesName, counterValue) when not (String.IsNullOrEmpty seriesName) && !seriesIndex |> Map.containsKey seriesName -> 
            xPosCounter := !xPosCounter + 1.
            let series = (!seriesIndex).[seriesName]
            series.Points.AddXY (!xPosCounter, counterValue) |> ignore
            while (series.Points.Count > maxPoints) do series.Points.RemoveAt 0
        | _ -> ()

        setChartBoundaries ()

    let pausedHandler message =
        match message with
        | Metric(seriesName, counterValue) when not (String.IsNullOrEmpty seriesName) && !seriesIndex |> Map.containsKey seriesName -> 
            let series = (!seriesIndex).[seriesName]
            xPosCounter := !xPosCounter + 1.
            series.Points.AddXY(!xPosCounter, 0.) |> ignore
            while (series.Points.Count > maxPoints) do series.Points.RemoveAt 0
        | _ -> ()
        setChartBoundaries()

    let setPauseButtonText paused =
        pauseButton.Text <- if paused then "RESUME -> " else "PAUSE  ||"

    (fun (mailbox : Actor<_>) ->
        let rec runningChartActor() = 
            actor {
                let! message = mailbox.Receive()
                match message with
                | TogglePause ->
                    setPauseButtonText true
                    return! pausedChartActor()
                | m ->
                    runningHandler m
                    return! runningChartActor()
            }
         and pausedChartActor() =
            actor {
                let! message = mailbox.Receive()

                match message with
                | TogglePause ->
                    setPauseButtonText false
                    return! runningChartActor()
                | m ->
                    pausedHandler m
                    return! pausedChartActor()
            }
        runningChartActor())

type PerformanceCounterActor(seriesName: string, performanceCounterGenerator: unit -> PerformanceCounter) as this =
    inherit UntypedActor()

    let mutable counter = null
    let cancelPublishing = new CancellationTokenSource()
    let subscriptions = HashSet<ActorRef>()

    override x.PreStart() =
        counter <- performanceCounterGenerator()
        scheduleCancellableTell (TimeSpan.FromMilliseconds 250.) (TimeSpan.FromMilliseconds 250.) GatherMetrics this.Self UntypedActor.Context.System.Scheduler cancelPublishing.Token |> ignore
        base.PreStart()

    override x.PostStop() =
        try
            cancelPublishing.Cancel false
            counter.Dispose()
            cancelPublishing.Dispose()
        with _ -> ()

        base.PostStop()
   
    override x.OnReceive message =
        match message :?> CounterMessage with
        | GatherMetrics ->
            let metric = Metric(seriesName, float <| counter.NextValue())
            subscriptions |> Seq.iter (fun actor -> actor <! metric)
        | SubscribeCounter subscription ->
            subscriptions.Add subscription.Subscriber |> ignore
        | UnsubscribeCounter subscription ->
            subscriptions.Remove subscription.Subscriber |> ignore

let performanceCounterCoordinatorActor chartingActor =
    let counterGenerators = Map.ofList [CounterType.Cpu, fun () -> new PerformanceCounter("Processor", "% Processor Time", "_Total", true)
                                        CounterType.Memory, fun () -> new PerformanceCounter("Memory", "% Committed Bytes In Use", true)
                                        CounterType.Disk, fun () -> new PerformanceCounter("LogicalDisk", "% Disk Time", "_Total", true)]
    
    let counterSeries = Map.ofList [CounterType.Cpu, fun () -> new Series(CounterType.Cpu.ToString (), ChartType = SeriesChartType.SplineArea, Color = Color.DarkGreen)
                                    CounterType.Memory, fun () -> new Series(CounterType.Memory.ToString (), ChartType = SeriesChartType.FastLine, Color = Color.MediumBlue)
                                    CounterType.Disk, fun () -> new Series(CounterType.Disk.ToString (), ChartType = SeriesChartType.SplineArea, Color = Color.DarkRed)]

    let counterActors = ref Map.empty // 'mutable' can be used with F# v4.0 http://blogs.msdn.com/b/fsharpteam/archive/2014/11/12/announcing-a-preview-of-f-4-0-and-the-visual-f-tools-in-vs-2015.aspx 

    let blah = Enum.GetName(typedefof<CounterType>, CounterType.Cpu)

    (fun (mailbox: Actor<_>) message -> 
        match message with
        | Watch counter when not ((!counterActors).ContainsKey counter) -> 
            let counterName = counter.ToString ()
            let actor = spawnObj mailbox.Context (sprintf "counterActor-%s" counterName) (<@ fun () -> new PerformanceCounterActor(counterName, counterGenerators.[counter]) @>)
            counterActors := (!counterActors).Add (counter, actor)
            chartingActor <! AddSeries(counterSeries.[counter] ())
            (!counterActors).[counter] <! SubscribeCounter({ CounterType = counter; Subscriber = chartingActor })
        | Watch counter ->
            chartingActor <! AddSeries(counterSeries.[counter] ())
            (!counterActors).[counter] <! SubscribeCounter({ CounterType = counter; Subscriber = chartingActor })
        | Unwatch counter when (!counterActors).ContainsKey(counter) -> 
            chartingActor <! RemoveSeries((counterSeries.[counter] ()).Name)
            (!counterActors).[counter] <! UnsubscribeCounter({ CounterType = counter; Subscriber = chartingActor })
        | _ -> ())


let buttonToggleActor coordinatorActor (button : Button) counterType isToggled =
    let isToggledOn = ref isToggled

    let flipToggle() = 
        isToggledOn := not <| !isToggledOn
        button.Text <- sprintf "%s (%s)" ((counterType.ToString ()).ToUpperInvariant ()) (if !isToggledOn then "ON" else "OFF")

    (fun (mailbox : Actor<_>) message ->
        match message with
        | Toggle when !isToggledOn ->
            coordinatorActor <! Unwatch(counterType)
            flipToggle()
        | Toggle when not <| !isToggledOn ->
            coordinatorActor <! Watch(counterType)
            flipToggle()
        | m -> mailbox.Unhandled m
    )