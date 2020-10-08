namespace Company.Function

open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Logging

open Castos.Podcatcher.Application


module PodcatcherTimer =
    [<FunctionName("TimerTriggerPodcatcher")>]
    let run([<TimerTrigger("%PodcatcherScheduleTriggerTime%")>]myTimer: TimerInfo, log: ILogger) =
        let feedsUrl = sprintf "%s/feeds" CastosApi
        log.LogInformation (sprintf "Using FeedsUrl: %s" feedsUrl)

        updateFeeds log.LogInformation feedsUrl
        |> Async.RunSynchronously

