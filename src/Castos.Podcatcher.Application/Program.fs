open System

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
    let content = getAsync "http://example.org/"
                  |> Async.RunSynchronously
    printfn "%s" content
    0 // return an integer exit code
