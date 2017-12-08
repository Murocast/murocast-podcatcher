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
                 MediaUrl: string
                 Length: int }

let private winToUtf (s:string) =
    let win1251 = Encoding.GetEncoding("windows-1251")
    let utf = UTF8Encoding()
    let encodedBytes = win1251.GetBytes s
    utf.GetString encodedBytes

let private flip4 f a b c d = f(d, c, b, a)

let parseDate = (flip4 (DateTime.ParseExact:string*string[]*CultureInfo*DateTimeStyles->DateTime)) 
                        DateTimeStyles.None
                        CultureInfo.InvariantCulture
                        [|"ddd, dd MMM yyyy HH:mm:ss GMT"; "ddd, dd MMM yyyy HH:mm:ss zzz"|]

let private parseNode (node:XmlNode) =
    let enclosureNode = (node.SelectSingleNode "enclosure")
    {
        Title = (node.SelectSingleNode "title").InnerText
        Link = (node.SelectSingleNode "link").InnerText
        Date = parseDate (node.SelectSingleNode "pubDate").InnerText
        Guid = (node.SelectSingleNode "guid").InnerText
        MediaUrl = enclosureNode.Attributes.["url"].Value
        Length = int enclosureNode.Attributes.["length"].Value
    }

let public getRssPosts (host:string) =
    let doc = XmlDocument()
    host
        |> getAsync
        |> Async.RunSynchronously
        //|> winToUtf
        |> doc.LoadXml
    doc.SelectNodes "/rss/channel/item"
        |> Seq.cast<XmlNode>
        |> Seq.map parseNode

let public getManyRssPosts: (list<string> -> list<RssPost>) =
    Seq.map getRssPosts
    >> Seq.concat
    >> Seq.toList