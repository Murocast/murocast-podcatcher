open System
open Castos.Podcatcher.Json
open System.Net.Http
open System.Text

open Rss

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
    Id: FeedId
    Url: string
    Name: string
    Category: string
    EpidsodesAmount: int
}

type EpisodeId = int
type Episode = {
    Id: EpisodeId
    Guid: string
    SubscriptionId: FeedId
    Url: string
    MediaUrl: string
    Title: string
    Length: int
    ReleaseDate: System.DateTime
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

type SupervisorMessage =
    | Start of MailboxProcessor<SupervisorMessage> * FeedListItemRendition list * AsyncReplyChannel<unit>
    | FetchCompleted

type SupervisorProgress =
    {
        Supervisor : MailboxProcessor<SupervisorMessage>
        ReplyChannel : AsyncReplyChannel<unit>
        Workers : MailboxProcessor<FeedListItemRendition> list
        PendingUrls : FeedListItemRendition Queue
        Completed : FeedListItemRendition list
        Dispatched : int
    }

type SupervisorStatus =
    | NotStarted
    | Running of SupervisorProgress
    | Finished

[<Literal>]
let CastosApi = "http://127.0.0.1/api"

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

let updateFeedAgent (supervisor : MailboxProcessor<SupervisorMessage>) = MailboxProcessor.Start(fun inbox ->
    let addEpisodeToSubscriptionAgent = MailboxProcessor.Start(fun inbox ->
        let rec loop() = async {
            let! (s:FeedListItemRendition), rendition = inbox.Receive()
            let json = mkjson rendition
            let! _ = postAsync (sprintf "%s/feeds/%O/episodes" CastosApi s.Id) json

            printfn "Add episode %s with mediaurl %s, guid %s and length %i to feed %s" rendition.Url rendition.MediaUrl rendition.Guid rendition.Length s.Name
            return! loop()
        }

        loop()
    )

    let rec waitForEpisodesAdded (agent:MailboxProcessor<FeedListItemRendition*AddEpisodeRendition>) =
        async {
            let! _ = Async.Sleep 2000 //Dirty hack
            match agent.CurrentQueueLength with
            | 0 -> ()
            | _ -> return! waitForEpisodesAdded agent
        }

    let rec loop() = async {
        let! (msg: FeedListItemRendition) = inbox.Receive()
        let url = msg.Url

        let content = getAsync (sprintf "%s/feeds/%O/episodes" CastosApi msg.Id )
                      |> Async.RunSynchronously

        let (episodes:Episode list) = unjson content

        getRssPosts url
        |> List.ofSeq
        |> List.filter (fun item ->
                            not(episodes |> List.exists (fun e -> item.Guid = e.Guid)) && (Option.isSome item.MediaUrl))
        |> List.map (fun e ->
            let rendition = { Guid = e.Guid
                              Title = e.Title
                              Url = e.Link
                              MediaUrl = Option.defaultValue "" e.MediaUrl
                              ReleaseDate = e.Date
                              Length = Option.defaultValue 0 e.Length
                              Episode = Option.defaultValue 0 e.Episode }
            addEpisodeToSubscriptionAgent.Post (msg, rendition))
        |> ignore

        let! _ = waitForEpisodesAdded addEpisodeToSubscriptionAgent
        supervisor.Post(FetchCompleted)

        return! loop()
    }

    loop()
)

let rec dispatch progress =
    match progress.PendingUrls.TryTake() with
    | None, _ -> progress
    | Some feed, queue -> match progress.Workers |> List.tryFind (fun worker -> worker.CurrentQueueLength = 0) with
                                  | Some idleWorker ->
                                        idleWorker.Post(feed)
                                        dispatch { progress with
                                                       PendingUrls = queue
                                                       Dispatched = progress.Dispatched + 1 }
                                  | None when progress.Workers.Length < 5 ->
                                        let newWorker = updateFeedAgent progress.Supervisor
                                        dispatch { progress with Workers = newWorker :: progress.Workers }
                                  | _ -> progress

let start supervisor replyChannel =
    {
        Supervisor = supervisor
        ReplyChannel = replyChannel
        Workers = []
        PendingUrls = Queue.Empty()
        Completed = []
        Dispatched = 0
    }

let enqueueFeeds feeds progress =
    let pending = progress.PendingUrls |> List.foldBack(fun url pending -> pending.Enqueue(url)) feeds
    { progress with PendingUrls = pending }

let handleStart supervisor feeds replyChannel =
    let progress = start supervisor replyChannel
                   |> enqueueFeeds feeds
                   |> dispatch
    if progress.PendingUrls.IsEmpty then
        Finished
    else
        Running progress

let complete progress =
    { progress with
        Dispatched = progress.Dispatched - 1 }

let handleFetchCompleted progress =
    let progress =
        progress
        |> complete
        |> dispatch
    if progress.PendingUrls.IsEmpty && progress.Dispatched = 0 then
        progress.ReplyChannel.Reply(())
        Finished
    else
        Running progress

let handleSupervisorMessage message state =
    match message with
    | Start (supervisor, subscriptions, replyChannel) ->
        match state with
        | NotStarted ->
            handleStart supervisor subscriptions replyChannel
        | _ -> failwith "Invalid state: Can't be started more than once."
    | FetchCompleted ->
        match state with
        | Running progress ->
            handleFetchCompleted progress
        | _ -> failwith "Invalid state - can't complete fetch before starting."

let updateFeeds feeds =
    let supervisor = MailboxProcessor<SupervisorMessage>.Start(fun inbox ->
        let rec loop state =
            async {
                let! message = inbox.Receive()
                match state |> handleSupervisorMessage message with
                | Finished -> return ()
                | newState -> return! loop newState
            }
        loop NotStarted)
    supervisor.PostAndAsyncReply(fun replyChannel -> Start(supervisor, feeds, replyChannel))

[<EntryPoint>]
let main argv =
    let content = getAsync (sprintf "%s/feeds" CastosApi)
                  |> Async.RunSynchronously

    unjson content
    |> updateFeeds
    |> Async.RunSynchronously

    0 // return an integer exit code
