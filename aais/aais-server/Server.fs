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

let mutable logPolicy = FactoryLogPolicy.create Warning
let private cache = new CacheService()

let private cacheServiceToken = new CancellationTokenSource()
let private cacheService = async {
    let source = "AdaptiveServer"
    let address = "127.0.0.1"
    let port = 1234
    let listener = new TcpListener(IPAddress.Parse(address), port)
    listener.Start(10)
    while not cacheServiceToken.IsCancellationRequested do
        use socket = listener.AcceptTcpClient()
        use reader = new StreamReader(socket.GetStream())
        use writer = new StreamWriter(socket.GetStream())
        let ASCII = new ASCIIEncoding()
        let command = reader.ReadLine()
        match command with
        | ParseRegex "^(store)(\s+)(.+)$" (head :: tail) ->
            let key = cache.store (List.ofArray (ASCII.GetBytes(head)))
            writer.WriteLine(key.ToString())
            writer.Flush()
        | ParseRegex "^(remove)(\s+)(\d+)$" (head :: tail) ->
            cache.remove (Int32.Parse(head))
        | ParseRegex "^(search)(\s+)(\d+)$" (head :: tail) ->
            let value = cache.search (Int32.Parse(head))
            writer.WriteLine(ASCII.GetString(List.toArray value))
            writer.Flush()
        | _ ->
            let log = [("Command \"" + command + "\" not supported.", Warning)]
            logPolicy.log source log
        reader.Close()
        writer.Close()
        socket.Close()}
Async.Start(cacheService, cacheServiceToken.Token)

let mutable running = true
while running do
    let source = "AdaptiveServerAdminConsole"
    let command = Console.ReadLine()
    match command with
    | ParseRegex "^(high)$" [] ->
        cache.high
    | ParseRegex "^(low)(\s+)(\d+)$" (size :: tail) ->
        cache.low (Int32.Parse(size))
    | ParseRegex "^(log)(\s+)(information|warning|error)" (level :: tail) ->
        match level with
        | "information" -> cache.log Information
        | "warning" -> cache.log Warning
        | "error" -> cache.log Error
        | _ -> ()
    | ParseRegex "^(quit)$" _ ->
        cacheServiceToken.Cancel()
        running <- false
    | _ ->
        let log = [("Command \"" + command + "\" not found.", Warning)]
        logPolicy.log source log