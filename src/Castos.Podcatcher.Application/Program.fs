open System
open Castos.Podcatcher.Json
open System.Net.Http
open System.Text

open Rss

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

let updateSubscriptionAgent = MailboxProcessor.Start(fun inbox ->
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

        return! loop()
    }

    loop()
)


[<EntryPoint>]
let main argv =
    let content = getAsync (sprintf "%s/subscriptions" CastosApi)
                  |> Async.RunSynchronously

    unjson content
    |> List.map updateSubscriptionAgent.Post
    |> ignore

    Console.ReadKey()
    |> ignore

    0 // return an integer exit code
