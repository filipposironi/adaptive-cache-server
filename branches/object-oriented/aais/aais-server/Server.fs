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

let address = "127.0.0.1"
let port = 1234
let listener = new TcpListener(IPAddress.Parse(address), port)
let cache = new CacheService()
let cacheServiceToken = new CancellationTokenSource()
let cacheService = async {
    while not cacheServiceToken.IsCancellationRequested do
        listener.Start 10
        use socket = listener.AcceptTcpClient()
        
        let cacheServiceHandler(socket: TcpClient) = async {
            use reader = new StreamReader(socket.GetStream())
            use writer = new StreamWriter(socket.GetStream())
            let ASCII = new ASCIIEncoding()
            let command = reader.ReadLine()
            match command with
            | "STORE" ->
                let value = List.ofArray(ASCII.GetBytes(reader.ReadLine().ToCharArray()))
                let key = cache.store value
                writer.WriteLine(key.ToString())
                writer.Flush()
            | "SEARCH" ->
                let key = Int32.Parse(reader.ReadLine())
                let value = cache.search key
                writer.WriteLine(ASCII.GetString(List.toArray value))
                writer.Flush()
            | "REMOVE" ->
                let key = Int32.Parse(reader.ReadLine())
                cache.remove key
            // TODO: avoid failing with an error
            | _ -> failwith "command not found"}
                
        Async.Start(cacheServiceHandler socket)}

Async.Start(cacheService, cacheServiceToken.Token)
let outbox = cache.getMailbox
while true do
    Console.Write "> "
    let line = Console.ReadLine()
    let command = line.Split([|' '|]).[0]
    let parameter = Int32.Parse(line.Split([|' '|]).[1])
    match command with
    | "size" ->
        outbox.Post(Msg(command, parameter))
        cache.update
    | "log" ->
        outbox.Post(Msg(command, parameter))
    // TODO: avoid failing with an error
    | _ -> failwith "command not found"
    