module Rss

open Network

open System.Globalization
open System.Xml
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

let toCorrectNumber s =
    ((int s).ToString("00", CultureInfo.GetCultureInfo("de-DE")))

let secondsToString s =
    let seconds = (int s)
    let timeSpan = TimeSpan.FromSeconds(float seconds)
    timeSpan.ToString("c")

let normalizeDurationString (duration:string) =
    let splitted = duration.Split([|':'|])
    match splitted.Length with
    | 1 -> (secondsToString splitted.[0])
    | 2 -> sprintf "00:%s:%s" (toCorrectNumber splitted.[0]) (toCorrectNumber splitted.[1])
    | 3 -> sprintf "%s:%s:%s" (toCorrectNumber splitted.[0]) (toCorrectNumber splitted.[1]) (toCorrectNumber splitted.[2])
    | _ -> failwith "WrongFormat"

let parseDuration duration =
    int (TimeSpan
            .ParseExact(normalizeDurationString duration, "c", CultureInfo.InvariantCulture)
            .TotalSeconds)

let updateDuration (node:XmlNode) nsManager rssPost =
    let durationNode = node.SelectSingleNode("itunes:duration", nsManager)
    if(isNull durationNode) then
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

let public getManyRssPosts: (list<string> -> list<RssPost>) =
    Seq.map getRssPosts
    >> Seq.concat
    >> Seq.toList