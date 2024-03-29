﻿(*
 * Copyright (c) 2010
 * Filippo Sironi (filippo.sironi@gmail.com)
 * Matteo Villa (villa.matteo@gmail.com)
 * ----------------------------------------------------------------------------
 *                        "THE BEER-WARE LICENSE"
 * Filippo Sironi and Matteo Villa wrote this file. As long as you retain this
 * notice you can do whatever you want with this stuff. If we meet some day,
 * and you think this stuff is worth it, you can buy us a beer in return.
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

let mutable protocol = 0

let mutable storeRegEx = "^(store:)(\s+)(.+)$"
let mutable removeRegEx = "^(remove:)(\s+)(\d+)$"
let mutable searchRegEx = "^(search:)(\s+)(\d+)$"
let mutable keyCommand = "key: "
let mutable valueCommand = "value: "
let mutable errorCommand = "error: "

let private serverEventLog = ConfigurationManager.AppSettings.Item("Server-Log")
let private cacheEventLog = ConfigurationManager.AppSettings.Item("Cache-Log")
let private consoleEventLog = ConfigurationManager.AppSettings.Item("Console-Log")

let private logger source messages =
    Assembly.LoadFrom(ConfigurationManager.AppSettings.Item("Server-Log-DLL")).GetType("Log").GetMethod("log").Invoke(null, [|source; messages|]) |> ignore

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
        protocol <- Int32.Parse(ConfigurationManager.AppSettings.Item("Default-Protocol"))
    | ProtocolError(message) ->
        let log = [(message, EventLogEntryType.Error)]
        logger serverEventLog log
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
                let log = [("Command \"" + command + "\" not supported.", EventLogEntryType.Warning)]
                logger serverEventLog log
                writer.WriteLine(errorCommand + fst (List.head log))
                writer.Flush()
            socket.Close()}
        Async.Start(loop socket)}
Async.Start(cacheService, cacheServiceToken.Token)

let mutable running = true
Console.WriteLine("Adaptive Cache Server 1.1")
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
        | "information" ->
            cache.log EventLogEntryType.Information
        | "warning" ->
            cache.log EventLogEntryType.Warning
        | "error" ->
            cache.log EventLogEntryType.Error
        | _ -> ()
    | ParseRegEx "^(show:)(\s+)(console|server|cache)(\s+)(\d+)$" (n :: source :: _) ->
        match source with
        | "console" ->
            let entries = getLastLogEntries consoleEventLog (Int32.Parse(n))
            Array.iter (fun (e: EventLogEntry) -> Console.WriteLine(e.Message)) entries
        | "server" ->
            let entries = getLastLogEntries serverEventLog (Int32.Parse(n))
            Array.iter (fun (e: EventLogEntry) -> Console.WriteLine(e.Message)) entries
        | "cache" ->
            let entries = getLastLogEntries cacheEventLog (Int32.Parse(n))
            Array.iter (fun (e: EventLogEntry) -> Console.WriteLine(e.Message)) entries
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
            protocol <- Int32.Parse(id)
        | ProtocolError(message) ->
            let log = [(message, EventLogEntryType.Error)]
            logger consoleEventLog log
    | ParseRegEx "^(config)$" _ ->
        Console.WriteLine("Cache context:")
        List.iter (fun (c: string) -> Console.WriteLine(c)) cache.config
        Console.WriteLine("Server context:")
        Console.WriteLine("Network context is \"Protocol " + protocol.ToString() + "\".")
    | ParseRegEx "^(quit)$" _ ->
        cacheServiceToken.Cancel()
        running <- false
    | ParseRegEx "^(help)$" _ ->
        Console.WriteLine("Adaptive Cache Server 1.1\n")
        Console.WriteLine("Copyright (c) 2010")
        Console.WriteLine("Filippo Sironi <filippo.sironi@gmail.com>")
        Console.WriteLine("Matteo Villa <villa.matteo@gmail.com>\n")
        Console.WriteLine("memory: high | low <size>\t\tset the memory context and cache size")
        Console.WriteLine("log: information | warning | error\tset the log level")
        Console.WriteLine("protocol: <number>\t\t\tset the communication protocol")
        Console.WriteLine("show: console | server | cache <number>\tprint the last <number> log messages")
        Console.WriteLine("config\t\t\t\t\tprint execution context information")
        Console.WriteLine("quit\t\t\t\t\tquit the application")
        Console.WriteLine("help\t\t\t\t\tshow this help message")
    | _ ->
        let log = [("Command \"" + command + "\" not found.", EventLogEntryType.Warning)]
        logger consoleEventLog log