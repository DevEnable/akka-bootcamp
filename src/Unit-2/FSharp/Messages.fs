[<AutoOpen>]
module Messages

open Akka.Actor
open System.Collections.Generic
open System.Windows.Forms.DataVisualization.Charting

type CounterType =
| Cpu = 1
| Memory = 2
| Disk = 3

type ChartMessage = 
| InitializeChart of initialSeries: Map<string, Series>
| AddSeries of series: Series
| RemoveSeries of seriesName : string
| Metric of series : string * counterValue : float
| TogglePause

type ActorSubscription = { CounterType : CounterType; Subscriber : ActorRef}

type CounterMessage = 
| GatherMetrics
| SubscribeCounter of ActorSubscription
| UnsubscribeCounter of ActorSubscription

type CoordinationMessage =
| Watch of counter: CounterType
| Unwatch of counter: CounterType

type ButtonMessage =
| Toggle