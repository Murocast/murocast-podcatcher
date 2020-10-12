module Castos.Podcatcher.Application

open System
open Castos.Podcatcher.Json
open System.Net.Http
open System.Text

open Microsoft.Extensions.Configuration
open System.IO

open Rss
open Parallel

type Queue<'a>(xs : 'a list, rxs : 'a list) =
    new() = Queue([], [])
    static member Empty() = new Queue<'a>([], [])

    member q.IsEmpty = (List.isEmpty xs) && (List.isEmpty rxs)
    member q.Enqueue(x) = Queue(xs, x::rxs)
    member q.TryTake() =
        if q.IsEmpty then None, q
        else
            match xs with
            | [] -> (Queue(List.rev rxs,[])).TryTake()
            | y::ys -> Some(y), (Queue(ys, rxs))

type FeedId = Guid
type FeedListItemRendition = {
    Id: System.Guid
    Url: string
    Name: string
    Category: string
    EpidsodesAmount: int
}

type EpisodeId = System.Guid
type Episode = {
    Id: EpisodeId
    Guid: string
    FeedId: FeedId
    Url: string
    MediaUrl: string
    Title: string
    Length: int
    ReleaseDate: System.DateTime
    Episode: int
}

type AddEpisodeRendition = {
    Title: string
    Guid: string
    Url: string
    ReleaseDate: System.DateTime
    MediaUrl: string
    Length: int
    Episode: int
}

let config =
    let path = DirectoryInfo(Directory.GetCurrentDirectory()).FullName
    printfn "Searching for configuration in %s" path
    ConfigurationBuilder()
        .SetBasePath(path)
        .AddJsonFile("appsettings.json", true, true)
        .AddEnvironmentVariables()
        .Build()

let CastosApi = config.["castosApi"]

let getAsync (url:string) =
    async {
        let httpClient = new System.Net.Http.HttpClient()
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        response.EnsureSuccessStatusCode () |> ignore
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        return content
    }

let postAsync (url:string) body =
    async {
        let content = new StringContent(body, Encoding.UTF8, "application/json");
        let httpClient = new System.Net.Http.HttpClient()
        let! response = httpClient.PostAsync(url, content) |> Async.AwaitTask
        response.EnsureSuccessStatusCode () |> ignore

        return! response.Content.ReadAsStringAsync() |> Async.AwaitTask
    }

let updateFeed (log:string->unit) (msg: FeedListItemRendition) = async {
    let addEpisodeToSubscription ((s:FeedListItemRendition), rendition) = async {
            let json = mkjson rendition
            let! _ = postAsync (sprintf "%s/feeds/%O/episodes" CastosApi s.Id) json

            sprintf "Add episode %s with mediaurl %s, guid %s and length %i to feed %s" rendition.Url rendition.MediaUrl rendition.Guid rendition.Length s.Name
            |> log
        }

    let url = msg.Url
    sprintf "Adding episodes for %s" url
    |> log

    let! content = getAsync (sprintf "%s/feeds/%O/episodes" CastosApi msg.Id )

    let (episodes:Episode list) = unjson content
    let f = fun (e:RssPost) ->
        let rendition = { Guid = e.Guid
                          Title = e.Title
                          Url = e.Link
                          MediaUrl = Option.defaultValue "" e.MediaUrl
                          ReleaseDate = e.Date
                          Length = Option.defaultValue 0 e.Length
                          Episode = Option.defaultValue 0 e.Episode }
        addEpisodeToSubscription (msg, rendition)
        |> Async.RunSynchronously

    let items = getRssPosts url
                |> List.ofSeq
                |> List.filter (fun item ->
                                    not(episodes |> List.exists (fun e -> item.Guid = e.Guid)) && (Option.isSome item.MediaUrl))

    doParallelWithThrottle 10 f items
    |> ignore
 }

let updateFeeds (log:string->unit) (url:string) = async {
    try
        let! content = getAsync url
        let items = unjson content
                    |> Seq.ofList

        let f a = Async.RunSynchronously (updateFeed log a)

        doParallelWithThrottle 10 f items
        |> ignore

        return ()
    with e -> printfn "%O" e
}

[<EntryPoint>]
let main argv =
    let log (s:string) = Console.WriteLine(s)
    let feedsUrl = sprintf "%s/feeds" CastosApi
    log (sprintf "Using FeedsUrl: %s" feedsUrl)

    updateFeeds log feedsUrl
    |> Async.RunSynchronously

    printf "Exited..."

    0 // return an integer exit code