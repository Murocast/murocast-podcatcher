open System
open Castos.Podcatcher.Json
open FeedParserCore

type SubscriptionId = Guid
type SubscriptionListItemRendition = {
    Id: SubscriptionId
    Url: string
    Name: string
    Category: string
    EpidsodesAmount: int
}

let getAsync (url:string) =
    async {
        let httpClient = new System.Net.Http.HttpClient()
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        response.EnsureSuccessStatusCode () |> ignore
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        return content
    }

let addEpisodeToSubscriptionAgent = MailboxProcessor.Start(fun inbox ->
    let rec loop() = async {
        let! msg = inbox.Receive()
        //TODO: Post to castos AddEpisode
        return! loop()
    }

    loop()
)

let updateSubscriptionAgent = MailboxProcessor.Start(fun inbox -> 
    let rec loop() = async {
        let! msg = inbox.Receive()
        let url = msg.Url

        //TODO get existing Episodes for subscription

        let items = FeedParser.Parse(url, FeedType.RSS)
                    |> List.ofSeq
                    |> List.filter (fun item -> true) //TODO: Filter for new Episodes (by url)
                    |> List.map (fun item -> addEpisodeToSubscriptionAgent.Post (msg, item))

        return! loop()
    }

    loop()
)

[<EntryPoint>]
let main argv =
    printfn "Hello World from F#!"
    let content = getAsync "http://localhost/api/subscriptions"
                  |> Async.RunSynchronously

    unjson content
    |> List.map updateSubscriptionAgent.Post
    |> ignore

    0 // return an integer exit code
