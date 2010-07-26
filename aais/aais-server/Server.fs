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

let private logSource = "AdaptiveServer"
let private logName = "Application"

let private cache = new CacheService()
let private cacheServiceToken = new CancellationTokenSource()

let private cacheService = async {
    let address = "127.0.0.1"
    let port = 1234
    let listener = new TcpListener(IPAddress.Parse(address), port)
    listener.Start(10)
    while not cacheServiceToken.IsCancellationRequested do
        use socket = listener.AcceptTcpClient()
        use reader = new StreamReader(socket.GetStream())
        use writer = new StreamWriter(socket.GetStream())
        let ASCII = new ASCIIEncoding()
        match reader.ReadLine() with
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
            let warning = "command not found"
            if not (EventLog.SourceExists(logSource)) then
                EventLog.CreateEventSource(logSource, logName)
            EventLog.WriteEntry(logSource, warning, EventLogEntryType.Warning)
            writer.WriteLine(warning)
            writer.Flush()
        reader.Close()
        writer.Close()
        socket.Close()}
Async.Start(cacheService, cacheServiceToken.Token)

let mutable running = true
while running do
    match Console.ReadLine() with
    | ParseRegex "(size)(\s+)(\d+)$" (head :: tail) ->
        cache.size (Int32.Parse(head))
    | ParseRegex "(log)(\s+)(enable|disable)" (head :: tail) ->
        cache.log (if head = "enable" then true else false)
    | ParseRegex "(quit)$" (head :: tail) ->
        cacheServiceToken.Cancel()
        running <- false
    | _ ->
        let warning = "command not found"
        if not (EventLog.SourceExists(logSource)) then
            EventLog.CreateEventSource(logSource, logName)
            EventLog.WriteEntry(logSource, warning, EventLogEntryType.Warning)
            Console.WriteLine(warning)