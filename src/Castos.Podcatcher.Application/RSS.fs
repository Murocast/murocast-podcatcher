module Rss

open Network

open System.Globalization
open System.Xml
open System.Xml.Linq
open System.Text
open System

type RssPost = { Title: string;
                 Link: string;
                 Date: DateTime
                 Guid: string
                 MediaUrl: string option
                 Length: int option
                 Episode: int option }

let private winToUtf (s:string) =
    let win1251 = Encoding.GetEncoding("windows-1251")
    let utf = UTF8Encoding()
    let encodedBytes = win1251.GetBytes s
    utf.GetString encodedBytes

let private flip4 f a b c d = f(d, c, b, a)

let parseDate = (flip4 (DateTime.ParseExact:string*string[]*CultureInfo*DateTimeStyles->DateTime)) 
                        DateTimeStyles.None
                        CultureInfo.InvariantCulture
                        [|"ddd, dd MMM yyyy HH:mm:ss GMT"; "ddd, dd MMM yyyy HH:mm:ss zzz"; "ddd, dd MMM yyyy HH:mm:ss EDT"|]

let toCorrectNumber (s:string) =
    let indexOfPoint = s.IndexOf "."
    match indexOfPoint with
    | -1 -> ((int s).ToString("00", CultureInfo.GetCultureInfo("de-DE")))
    | _ -> ((int (s.Substring(0, indexOfPoint))).ToString("00", CultureInfo.GetCultureInfo("de-DE")))

let secondsToString s =
    let seconds = (int s)
    let timeSpan = TimeSpan.FromSeconds(float seconds)
    timeSpan.ToString("c")

type TimePart =
    | Milliseconds
    | Second
    | Minute
    | Hour
    | Day

let getNumberWithOverflow divider s overflow =
    let n = (toCorrectNumber s) |> int
    let n = n + overflow

    let newOverflow = n / divider
    let n = n - (newOverflow * divider)
    (n, newOverflow)

let rec normalizeTimeSpanString parts partType overflow =
    match parts, partType with
    | s::rest, Milliseconds ->
        let (n, newOverflow) = getNumberWithOverflow 1000 s overflow
        n :: normalizeTimeSpanString rest Second newOverflow
    | s::rest, Second ->
        let (n, newOverflow) = getNumberWithOverflow 60 s overflow
        n :: normalizeTimeSpanString rest Minute newOverflow
    | s::rest, Minute ->
        let (n, newOverflow) = getNumberWithOverflow 60 s overflow
        n :: normalizeTimeSpanString rest Hour newOverflow
    | s::rest, Hour ->
        let (n, newOverflow) = getNumberWithOverflow 24 s overflow
        n :: normalizeTimeSpanString rest Day newOverflow
    | s::rest, Day ->
        let (n, newOverflow) = getNumberWithOverflow Int32.MaxValue s overflow
        n :: normalizeTimeSpanString rest Day newOverflow
    | [], _ when overflow > 0 ->
        match partType with
        | Milliseconds ->
            let (n, newOverflow) = getNumberWithOverflow 1000 "0" overflow
            n :: normalizeTimeSpanString [] Second newOverflow
        | Second ->
            let (n, newOverflow) = getNumberWithOverflow 60 "0" overflow
            n :: normalizeTimeSpanString [] Minute newOverflow
        | Minute ->
            let (n, newOverflow) = getNumberWithOverflow 60 "0" overflow
            n :: normalizeTimeSpanString [] Hour newOverflow
        | Hour ->
            let (n, newOverflow) = getNumberWithOverflow 24 "0" overflow
            n :: normalizeTimeSpanString [] Day newOverflow
        | Day ->
            [ overflow ]
    | _ -> []

let getTimeSpanParts ar =
    let rev = Array.rev ar
              |> List.ofArray

    let result = normalizeTimeSpanString rev Second 0
                 |> Array.ofList

    Array.rev result

let normalizeDurationString (duration:string) =
    let splitted = duration.Split([|':'|])
    match splitted.Length with
    | 1 -> (secondsToString splitted.[0])
    | _ ->
        let parts = getTimeSpanParts splitted
        match parts.Length with
        | 2 -> sprintf "00:%i:%i" parts.[0] parts.[1]
        | 3 -> sprintf "%i:%i:%i" parts.[0] parts.[1] parts.[2]
        //30:23:42 where 30 first number are minutes
        | 4 when splitted.Length = 3 ->
            let parts = getTimeSpanParts [|(splitted.[0]); (splitted.[1])|]
            sprintf "00:%i:%i" parts.[0] parts.[1]
        | _ -> failwith "WrongFormat"

let parseDuration duration =
    int (TimeSpan
            .ParseExact(normalizeDurationString duration, "c", CultureInfo.InvariantCulture)
            .TotalSeconds)

let updateDuration (node:XmlNode) nsManager rssPost =
    let durationNode = node.SelectSingleNode("itunes:duration", nsManager)
    if((isNull durationNode) || System.String.IsNullOrWhiteSpace(durationNode.InnerText)) then
        rssPost
    else
        { rssPost with
                    Length = Some (parseDuration durationNode.InnerText) }

let updateMediaUrl (node:XmlNode) rssPost =
    let enclosureNode = (node.SelectSingleNode "enclosure")
    if(isNull enclosureNode) then
        rssPost
    else
        { rssPost with
              MediaUrl = Some enclosureNode.Attributes.["url"].Value }

let updateEpisode (node:XmlNode) nsManager rssPost =
    let episodeNode = node.SelectSingleNode("itunes:episode", nsManager)
    if(isNull episodeNode) then
        rssPost
    else
        let (success, episode) = System.Int32.TryParse episodeNode.InnerText
        if success then
            { rssPost with
                Episode = Some episode }
        else
            rssPost

let private parseNode nsManager (node:XmlNode) =
    try
        {  Title = (node.SelectSingleNode "title").InnerText
           Link = (node.SelectSingleNode "link").InnerText
           Date = parseDate (node.SelectSingleNode "pubDate").InnerText
           Guid = (node.SelectSingleNode "guid").InnerText
           MediaUrl = None
           Length = None
           Episode = None }
        |> updateDuration node nsManager
        |> updateMediaUrl node
        |> updateEpisode node nsManager
        |> Some
    with
        | e ->
              printfn "Xml: %s" (XElement.Parse(node.OuterXml).ToString())
              printfn "Exn: %A" e
              None


let public getRssPosts (host:string) =
    let doc = XmlDocument()
    let nsManager = XmlNamespaceManager(doc.NameTable)
    nsManager.AddNamespace("itunes","http://www.itunes.com/dtds/podcast-1.0.dtd")
    host
        |> getAsync
        |> Async.RunSynchronously
        //|> winToUtf
        |> doc.LoadXml
    doc.SelectNodes "/rss/channel/item"
        |> Seq.cast<XmlNode>
        |> Seq.map (parseNode nsManager)
        |> Seq.filter (fun n -> n.IsSome)
        |> Seq.map (fun n -> n.Value)

let public getManyRssPosts: (list<string> -> list<RssPost>) =
    Seq.map getRssPosts
    >> Seq.concat
    >> Seq.toList