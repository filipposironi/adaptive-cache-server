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
open System.Text
open System.Threading
open System.Xml
open System.Xml.Linq

open Helpers
open LogPolicies
open MemoryPolicies
open CacheService

let mutable logPolicy = FactoryLogPolicy.Create Warning

let mutable storeRegEx = "^(store:)(\s+)(.+)$"
let mutable removeRegEx = "^(remove:)(\s+)(\d+)$"
let mutable searchRegEx = "^(search:)(\s+)(\d+)$"
let mutable keyCommand = "key: "
let mutable valueCommand = "value: "
let mutable errorCommand = "error: "

let private serverEventLog = ConfigurationManager.AppSettings.Item("Server-Log")
let private consoleEventLog = ConfigurationManager.AppSettings.Item("Console-Log")

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
        logPolicy.log serverEventLog [(message, Error)]
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
                    writer.WriteLine(errorCommand)
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
                    writer.WriteLine(errorCommand)
                    writer.Flush()
                | Some value ->
                    writer.WriteLine(valueCommand + ASCII.GetString(List.toArray value))
                    writer.Flush()
            | _ ->
                let log = [("Command \"" + command + "\" not supported.", Warning)]
                writer.WriteLine(errorCommand + fst (List.head log))
                writer.Flush()
                logPolicy.log serverEventLog log
            reader.Close()
            writer.Close()
            socket.Close()}
        Async.Start(loop socket)}
Async.Start(cacheService, cacheServiceToken.Token)

let mutable running = true
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
        | "information" -> cache.log Information
        | "warning" -> cache.log Warning
        | "error" -> cache.log Error
        | _ -> ()
    | ParseRegEx "^(show:)(\s+)(console|server)(\s+)(\d+)$" (n :: source :: _) ->
        match source with
        | "console" ->
            let entries = getLastLogEntries consoleEventLog (Int32.Parse(n))
            for e in entries do
                Console.WriteLine(e.Message)
        | "server" ->
            let entries = getLastLogEntries serverEventLog (Int32.Parse(n))
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
            logPolicy.log consoleEventLog [(message, Error)]
    | ParseRegEx "^(config)$" _ ->
        for config in cache.config do
            Console.WriteLine(config)
    | ParseRegEx "^(quit)$" _ ->
        cacheServiceToken.Cancel()
        running <- false
    | _ ->
        let log = [("Command \"" + command + "\" not found.", Warning)]
        logPolicy.log consoleEventLog log