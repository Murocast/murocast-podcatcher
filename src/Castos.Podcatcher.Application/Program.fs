open System
open Castos.Podcatcher.Json

type SubscriptionId = Guid
type EpisodeId = int

type Episode = {
    Id: EpisodeId
    SubscriptionId: SubscriptionId
    MediaUrl: string
    Title: string
    ReleaseDate: System.DateTime
}

type Subscription = {
    Id: SubscriptionId
    Url: string
    Name: string
    Category: string
    Active: bool
    Episodes: Episode list
}

let getAsync (url:string) =
    async {
        let httpClient = new System.Net.Http.HttpClient()
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        response.EnsureSuccessStatusCode () |> ignore
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        return content
    }

[<EntryPoint>]
let main argv =
    printfn "Hello World from F#!"
    let content = getAsync "http://localhost/api/subscriptions"
                  |> Async.RunSynchronously

    let (typedContent:Subscription list) = unjson content

    printfn "%O" typedContent
    0 // return an integer exit code
