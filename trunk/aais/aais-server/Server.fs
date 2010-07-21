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
        try
            listener.Start 10
            try
                let socket = listener.AcceptTcpClient()
                let cacheServiceHandler(socket: TcpClient) = async {
                    try
                        let reader = new StreamReader(socket.GetStream())
                        let writer = new StreamWriter(socket.GetStream())
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
                        | _ -> failwith "command not found"
                        socket.Close()
                    with
                    | :? ObjectDisposedException as e -> Console.WriteLine e.Message
                    | :? IOException as e -> Console.WriteLine e.Message
                    | :? OverflowException as e -> Console.WriteLine e.Message
                }
                Async.Start(cacheServiceHandler socket)
            with
            | :? InvalidOperationException as e ->
                Console.WriteLine e.Message
                raise(new SocketException())
        finally
            listener.Stop()
}

Async.Start(cacheService, cacheServiceToken.Token)
let outbox = cache.getMailbox
while true do
    try
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
    with
    | :? OverflowException as e -> Console.WriteLine e.Message