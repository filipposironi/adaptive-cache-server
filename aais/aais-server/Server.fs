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
open CacheService

let mutable logLevel = "warning"
let private cache = new CacheService()

let private cacheServiceToken = new CancellationTokenSource()
let private cacheService = async {
    let logSource = "AdaptiveServer"
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
        | ParseRegex "(store)(\s+)(.+)$" (head :: tail) ->
            let key = cache.store (List.ofArray (ASCII.GetBytes(head)))
            writer.WriteLine(key.ToString())
            writer.Flush()
        | ParseRegex "(remove)(\s+)(\d+)$" (head :: tail) ->
            cache.remove (Int32.Parse(head))
        | ParseRegex "(search)(\s+)(\d+)$" (head :: tail) ->
            let value = cache.search (Int32.Parse(head))
            writer.WriteLine(ASCII.GetString(List.toArray value))
            writer.Flush()
        | _ ->
            writeLogEntry logSource Warning ("Command \"" + command + "\" not supported.")
        reader.Close()
        writer.Close()
        socket.Close()}
Async.Start(cacheService, cacheServiceToken.Token)

let mutable running = true
while running do
    let logSource = "AdaptiveServerAdminConsole"
    let command = Console.ReadLine()
    match command with
    | ParseRegex "(size)(\s+)(\d+)$" (size :: tail) ->
        cache.size (Int32.Parse(size))
    | ParseRegex "(log)(\s+)(info|warning|error)" (level :: tail) ->
        cache.log level
    | ParseRegex "(quit)$" _ ->
        cacheServiceToken.Cancel()
        running <- false
    | _ ->
        writeLogEntry logSource Warning ("Command \"" + command + "\" not found.")
    | _ -> ()