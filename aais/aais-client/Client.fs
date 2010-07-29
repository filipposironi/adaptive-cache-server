#light

module Client

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text

let private address = "127.0.0.1"
let private port = 1234

let store words =
    let rec loop words (keys: int list) =
        match words with
        | [] -> keys
        | (head :: tail) ->
            use client = new TcpClient(address, port)
            use reader = new StreamReader(client.GetStream())
            use writer = new StreamWriter(client.GetStream())
            writer.WriteLine("store " + head)
            writer.Flush()
            let keys = keys @ [Int32.Parse(reader.ReadLine())]
            loop tail keys

    loop words []

let keys = store ["pera"; "mela"; "fragola"; "banana"; "albicocca"]
for k in keys do
    Console.WriteLine(k)

let search keys =
    let rec loop keys (words: string list) =
        match keys with
        | [] -> words
        | (head :: tail) ->
            use client = new TcpClient(address, port)
            use reader = new StreamReader(client.GetStream())
            use writer = new StreamWriter(client.GetStream())
            writer.WriteLine("search " + head.ToString())
            writer.Flush()
            let words = words @ [reader.ReadLine()]
            loop tail words

    loop keys []

let words = search keys
for w in words do
    Console.WriteLine(w)

Console.ReadKey() |> ignore