#light

module Main

open System
open System.IO
open System.Collections
open System.Net
open System.Net.Sockets
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters.Binary
open System.Text
open System.Threading

open CacheService

let cache = new CacheService()
let cacheServiceToken = new CancellationTokenSource()
let cacheService = async {
    while not cacheServiceToken.IsCancellationRequested do
        let listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 1234)
        listener.Start()
        let socket = listener.AcceptTcpClient()
        let cacheServiceHandler (socket: TcpClient) = async {
            let reader = new StreamReader(socket.GetStream())
            let writer = new StreamWriter(socket.GetStream())
            let ASCII = new ASCIIEncoding()
            let command = reader.ReadLine()
            match command with
            | "STORE" ->
                let value = ASCII.GetBytes(reader.ReadLine().ToCharArray()) |> List.ofArray
                let key = value |> cache.store
                writer.WriteLine(key.ToString())
                writer.Flush()
            | "SEARCH" ->
                let key = Int32.Parse(reader.ReadLine())
                let value = key |> cache.search
                writer.WriteLine(ASCII.GetString(value |> List.toArray))
                writer.Flush()
            | "REMOVE" ->
                let key = Int32.Parse(reader.ReadLine())
                key |> cache.remove
            | _ -> failwith "command not found"
            socket.Close() |> ignore
        }
        Async.Start(cacheServiceHandler socket)
}

Async.Start(cacheService, cacheServiceToken.Token)
let outbox = cache.getMailbox
while true do
    Console.Write("> ")
    let line = Console.ReadLine()
    let command = line.Split([|' '|]).[0]
    let parameter = Int32.Parse(line.Split([|' '|]).[1])
    match command with
    | "size" -> outbox.Post(Msg(command, parameter))
    | _ -> failwith "command not found"