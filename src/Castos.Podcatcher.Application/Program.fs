open System
open Castos.Podcatcher.Json
open FeedParserCore
open System.Net.Http
open System.Text

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
    SubscriptionId: SubscriptionId
    MediaUrl: string
    Title: string
    ReleaseDate: System.DateTime
}

type AddEpisodeRendition = {
    Title: string
    Url: string
    ReleaseDate: System.DateTime
}

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
        let! (s:SubscriptionListItemRendition), (e: FeedItem) = inbox.Receive()
        let rendition = { Title = e.Title
                          Url = e.Link
                          ReleaseDate = e.PublishDate }
        let json = mkjson rendition
        let! _ = postAsync (sprintf "http://localhost/api/subscriptions/%O/episodes" s.Id) json

        printfn "Add episode %s to subscription %s" e.Link s.Name
        return! loop()
    }

    loop()
)

let updateSubscriptionAgent = MailboxProcessor.Start(fun inbox ->
    let rec loop() = async {
        let! (msg: SubscriptionListItemRendition) = inbox.Receive()
        let url = msg.Url

        let content = getAsync (sprintf "http://localhost/api/subscriptions/%O/episodes" msg.Id )
                      |> Async.RunSynchronously

        let (episodes:Episode list) = unjson content

        FeedParser.Parse(url, FeedType.Atom)
        |> List.ofSeq
        |> List.filter (fun item -> not(episodes |> List.exists (fun e -> item.Link.ToLowerInvariant() = e.MediaUrl.ToLowerInvariant())))
        |> List.map (fun item -> addEpisodeToSubscriptionAgent.Post (msg, item))
        |> ignore

        return! loop()
    }

    loop()
)

[<EntryPoint>]
let main argv =
    let content = getAsync "http://localhost/api/subscriptions"
                  |> Async.RunSynchronously

    unjson content
    |> List.map updateSubscriptionAgent.Post
    |> ignore

    Console.ReadKey()
    |> ignore

    0 // return an integer exit code
