[<AutoOpen>]
module Messages

open System.Collections.Generic
open System.Windows.Forms.DataVisualization.Charting

type ChartMessage = 
| InitializeChart of initialSeries: Map<string, Series>
| AddSeries of series: Series