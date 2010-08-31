(* 
 * Copyright (c) 2010
 * Filippo Sironi (filippo.sironi@gmail.com)
 * Matteo Villa (villa.matteo@gmail.com)
 * ----------------------------------------------------------------------------
 *                        "THE BEER-WARE LICENSE"
 * Filippo Sironi and Matteo Villa wrote this file. As long as you retain this
 * notice you can do whatever you want with this stuff. If we meet some day, and
 * you think this stuff is worth it, you can buy us a beer in return.
 * ----------------------------------------------------------------------------
 *)

#light

module Server

open System
open System.Configuration
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets
open System.Reflection
open System.Text
open System.Threading
open System.Xml
open System.Xml.Linq

open Helpers
open MemoryPolicies
open CacheService

let mutable storeRegEx = "^(store:)(\s+)(.+)$"
let mutable removeRegEx = "^(remove:)(\s+)(\d+)$"
let mutable searchRegEx = "^(search:)(\s+)(\d+)$"
let mutable keyCommand = "key: "
let mutable valueCommand = "value: "
let mutable errorCommand = "error: "

let private serverEventLog = ConfigurationManager.AppSettings.Item("Server-Log")
let private consoleEventLog = ConfigurationManager.AppSettings.Item("Console-Log")
let private cacheEventLog = ConfigurationManager.AppSettings.Item("Cache-Log")

let assembly = Assembly.LoadFrom(ConfigurationManager.AppSettings.Item("Server-Log-DLL"))
let container = assembly.GetType("Log")
let f = container.GetMethod("log")
let log source messages =
    f.Invoke(null, [|source; messages|]) |> ignore

let private cache = new CacheService()
let private cacheServiceToken = new CancellationTokenSource()
let private cacheService = async {
    let address = ConfigurationManager.AppSettings.Item("IP-Address")
    let port = Int32.Parse(ConfigurationManager.AppSettings.Item("TCP-Port"))
    match readProtocol (ConfigurationManager.AppSettings.Item("Default-Protocol")) with
    | Protocol(newStoreRegEx, newRemoveRegEx, newSearchRegEx, newKeyCommand, newValueCommand, newErrorCommand) ->
        storeRegEx <- newStoreRegEx
        removeRegEx <- newRemoveRegEx
        searchRegEx <- newSearchRegEx
        keyCommand <- newKeyCommand
        valueCommand <- newValueCommand
        errorCommand <- newErrorCommand
    | ProtocolError(message) ->
        log serverEventLog [(message, EventLogEntryType.Error)]
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
            | ParseRegEx storeRegEx (value :: _) ->
                let request = async {return! cache.store (List.ofArray (ASCII.GetBytes(value)))}
                match Async.RunSynchronously(request) with
                | None ->
                    writer.WriteLine(errorCommand + "value not stored.")
                    writer.Flush()
                | Some key ->
                    writer.WriteLine(keyCommand + key.ToString())
                    writer.Flush()
            | ParseRegEx removeRegEx (key :: _) ->
                cache.remove (Int32.Parse(key))
            | ParseRegEx searchRegEx (key :: _) ->
                let request = async {return! cache.search (Int32.Parse(key))}
                match Async.RunSynchronously(request) with
                | None ->
                    writer.WriteLine(errorCommand + "key not found.")
                    writer.Flush()
                | Some value ->
                    writer.WriteLine(valueCommand + ASCII.GetString(List.toArray value))
                    writer.Flush()
            | _ ->
                writer.WriteLine(errorCommand + "Command \"" + command + "\" not supported.")
                writer.Flush()
                log serverEventLog [("Command \"" + command + "\" not supported.", EventLogEntryType.Warning)]
            reader.Close()
            writer.Close()
            socket.Close()}
        Async.Start(loop socket)}
Async.Start(cacheService, cacheServiceToken.Token)

let mutable running = true
Console.WriteLine("Awesome adaptive cache server, version 1.1")
Console.WriteLine("Type 'help' to see the commands list")
while running do
    Console.Write("# ")
    let command = Console.ReadLine()
    match command with
    | ParseRegEx "^(memory:)(\s+)(high)$" _ ->
        cache.high
    | ParseRegEx "^(memory:)(\s+)(low)(\s+)(\d+)$" (size :: _) ->
        cache.low (Int32.Parse(size))
    | ParseRegEx "^(log:)(\s+)(information|warning|error)" (level :: _) ->
        match level with
        | "information" -> cache.log EventLogEntryType.Information
        | "warning" -> cache.log EventLogEntryType.Warning
        | "error" -> cache.log EventLogEntryType.Error
        | _ -> ()
    | ParseRegEx "^(show:)(\s+)(console|server|cache)(\s+)(\d+)$" (n :: source :: _) ->
        match source with
        | "console" ->
            let entries = getLastLogEntries consoleEventLog (Int32.Parse(n))
            for e in entries do
                Console.WriteLine(e.Message)
        | "server" ->
            let entries = getLastLogEntries serverEventLog (Int32.Parse(n))
            for e in entries do
                Console.WriteLine(e.Message)
        | "cache" ->
            let entries = getLastLogEntries cacheEventLog (Int32.Parse(n))
            for e in entries do
                Console.WriteLine(e.Message)
        | _ -> ()
    | ParseRegEx "^(protocol:)(\s+)(.+)$" (id :: _) ->
        match readProtocol id with
        | Protocol(newStoreRegEx, newRemoveRegEx, newSearchRegEx, newKeyCommand, newValueCommand, newErrorCommand) ->
            storeRegEx <- newStoreRegEx
            removeRegEx <- newRemoveRegEx
            searchRegEx <- newSearchRegEx
            keyCommand <- newKeyCommand
            valueCommand <- newValueCommand
            errorCommand <- newErrorCommand
        | ProtocolError(message) ->
            log consoleEventLog [(message, EventLogEntryType.Error)]
    | ParseRegEx "^(config)$" _ ->
        for config in cache.config do
            Console.WriteLine(config)
    | ParseRegEx "^(help)$" _ ->
        Console.WriteLine("Awesome adaptive cache server, version 1.1")
        Console.WriteLine()
        Console.WriteLine(" memory: high | low <maxSize> \t\t set the memory context")
        Console.WriteLine(" log: information | warning | error \t set the log level policy")
        Console.WriteLine(" protocol: <N> \t\t\t\t set the communication protocol version")
        Console.WriteLine(" show: console | server | cache <N> \t print the last N log entries")
        Console.WriteLine(" help \t\t\t\t\t show this help message")
        Console.WriteLine(" config \t\t\t\t print the current server configuration")
        Console.WriteLine(" quit \t\t\t\t\t quit the application")
        Console.WriteLine()
    | ParseRegEx "^(quit)$" _ ->
        cacheServiceToken.Cancel()
        running <- false
    | _ ->
        log consoleEventLog [("Command \"" + command + "\" not found.", EventLogEntryType.Warning)]