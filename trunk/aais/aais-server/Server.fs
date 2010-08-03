#light

module Server

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Threading

open Helpers
open LogPolicies
open MemoryPolicies
open CacheService

let mutable logPolicy = FactoryLogPolicy.Create Warning
let private cache = new CacheService()

let private cacheServiceToken = new CancellationTokenSource()
let private cacheService = async {
    let source = "AdaptiveServer"
    let address = "127.0.0.1"
    let port = 1234
    let listener = new TcpListener(IPAddress.Parse(address), port)
    listener.Start(10)
    while not cacheServiceToken.IsCancellationRequested do
        let socket = listener.AcceptTcpClient()
        let loop (socket: TcpClient) = async {
            use reader = new StreamReader(socket.GetStream())
            use writer = new StreamWriter(socket.GetStream())
            let ASCII = new ASCIIEncoding()
            let command = reader.ReadLine()
            match command with
            | ParseRegex "^(store:)(\s+)(.+)$" (head :: tail) ->
                let request = async {return! cache.store (List.ofArray (ASCII.GetBytes(head)))}
                match Async.RunSynchronously(request) with
                | None ->
                    writer.WriteLine("error: ")
                    writer.Flush()
                | Some key ->
                    writer.WriteLine("key: " + key.ToString())
                    writer.Flush()
            | ParseRegex "^(remove:)(\s+)(\d+)$" (head :: tail) ->
                cache.remove (Int32.Parse(head))
            | ParseRegex "^(search:)(\s+)(\d+)$" (head :: tail) ->
                let request = async {return! cache.search (Int32.Parse(head))}
                match Async.RunSynchronously(request) with
                | None ->
                    writer.WriteLine("error: ")
                    writer.Flush()
                | Some value ->
                    writer.WriteLine("value: " + ASCII.GetString(List.toArray value))
                    writer.Flush()
            | _ ->
                let log = [("Command \"" + command + "\" not supported.", Warning)]
                logPolicy.log source log
            reader.Close()
            writer.Close()
            socket.Close()}
        Async.Start(loop socket)}
Async.Start(cacheService, cacheServiceToken.Token)

let mutable running = true
while running do
    let source = "AdaptiveServerAdminConsole"
    let command = Console.ReadLine()
    match command with
    | ParseRegex "^(memory high)$" _ ->
        cache.high
    | ParseRegex "^(memory low)(\s+)(\d+)$" (size :: tail) ->
        cache.low (Int32.Parse(size))
    | ParseRegex "^(log)(\s+)(information|warning|error)" (level :: tail) ->
        match level with
        | "information" -> cache.log Information
        | "warning" -> cache.log Warning
        | "error" -> cache.log Error
        | _ -> ()
    | ParseRegex "^(config)$" _ ->
        for config in cache.config do
            Console.WriteLine(config)
    | ParseRegex "^(quit)$" _ ->
        cacheServiceToken.Cancel()
        running <- false
    | _ ->
        let log = [("Command \"" + command + "\" not found.", Warning)]
        logPolicy.log source log