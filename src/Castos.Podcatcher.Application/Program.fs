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

type SubscriptionId = Guid
type SubscriptionListItemRendition = {
    Id: SubscriptionId
    Url: string
    Name: string
    Category: string
    EpidsodesAmount: int
}

type EpisodeId = int
type Episode = {
    Id: EpisodeId
    Guid: string
    SubscriptionId: SubscriptionId
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
}

type SupervisorMessage =
    | Start of MailboxProcessor<SupervisorMessage> * SubscriptionListItemRendition list * AsyncReplyChannel<unit>
    | FetchCompleted

type SupervisorProgress =
    {
        Supervisor : MailboxProcessor<SupervisorMessage>
        ReplyChannel : AsyncReplyChannel<unit>
        Workers : MailboxProcessor<SubscriptionListItemRendition> list
        PendingUrls : SubscriptionListItemRendition Queue
        Completed : SubscriptionListItemRendition list
        Dispatched : int
    }

type SupervisorStatus =
    | NotStarted
    | Running of SupervisorProgress
    | Finished

[<Literal>]
let CastosApi = "http://192.168.178.42/api"

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

let updateSubscriptionAgent (supervisor : MailboxProcessor<SupervisorMessage>) = MailboxProcessor.Start(fun inbox ->
    let addEpisodeToSubscriptionAgent = MailboxProcessor.Start(fun inbox ->
        let rec loop() = async {
            let! (s:SubscriptionListItemRendition), rendition = inbox.Receive()
            let json = mkjson rendition
            let! _ = postAsync (sprintf "%s/subscriptions/%O/episodes" CastosApi s.Id) json

            printfn "Add episode %s with mediaurl %s, guid %s and length %i to subscription %s" rendition.Url rendition.MediaUrl rendition.Guid rendition.Length s.Name
            return! loop()
        }

        loop()
    )

    let rec waitForEpisodesAdded (agent:MailboxProcessor<SubscriptionListItemRendition*AddEpisodeRendition>) =
        match agent.CurrentQueueLength with
        | 0 -> ()
        | _ -> waitForEpisodesAdded agent

    let rec loop() = async {
        let! (msg: SubscriptionListItemRendition) = inbox.Receive()
        let url = msg.Url

        let content = getAsync (sprintf "%s/subscriptions/%O/episodes" CastosApi msg.Id )
                      |> Async.RunSynchronously

        let (episodes:Episode list) = unjson content

        getRssPosts url
        |> List.ofSeq
        |> List.filter (fun item ->
                            not(episodes |> List.exists (fun e -> item.Guid = e.Guid)) && (Option.isSome item.MediaUrl) && (Option.isSome item.Length))
        |> List.map (fun e ->
            let rendition ={ Guid = e.Guid
                             Title = e.Title
                             Url = e.Link
                             MediaUrl = Option.get e.MediaUrl
                             ReleaseDate = e.Date
                             Length = Option.get e.Length }
            addEpisodeToSubscriptionAgent.Post (msg, rendition))
        |> ignore

        waitForEpisodesAdded addEpisodeToSubscriptionAgent
        supervisor.Post(FetchCompleted)

        return! loop()
    }

    loop()
)

let rec dispatch progress =
    match progress.PendingUrls.TryTake() with
    | None, _ -> progress
    | Some subscription, queue -> match progress.Workers |> List.tryFind (fun worker -> worker.CurrentQueueLength = 0) with
                                  | Some idleWorker ->
                                        idleWorker.Post(subscription)
                                        dispatch { progress with
                                                       PendingUrls = queue
                                                       Dispatched = progress.Dispatched + 1 }
                                  | None when progress.Workers.Length < 5 ->
                                        let newWorker = updateSubscriptionAgent progress.Supervisor
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

let enqueueSubscriptions subscriptions progress =
    let pending = progress.PendingUrls |> List.foldBack(fun url pending -> pending.Enqueue(url)) subscriptions
    { progress with PendingUrls = pending }

let handleStart supervisor subscriptions replyChannel =
    let progress = start supervisor replyChannel
                   |> enqueueSubscriptions subscriptions
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

let updateSubscriptions subscriptions =
    let supervisor = MailboxProcessor<SupervisorMessage>.Start(fun inbox ->
        let rec loop state =
            async {
                let! message = inbox.Receive()
                match state |> handleSupervisorMessage message with
                | Finished -> return ()
                | newState -> return! loop newState
            }
        loop NotStarted)
    supervisor.PostAndAsyncReply(fun replyChannel -> Start(supervisor, subscriptions, replyChannel))

[<EntryPoint>]
let main argv =
    let content = getAsync (sprintf "%s/subscriptions" CastosApi)
                  |> Async.RunSynchronously

    unjson content
    |> updateSubscriptions
    |> Async.RunSynchronously

    0 // return an integer exit code
