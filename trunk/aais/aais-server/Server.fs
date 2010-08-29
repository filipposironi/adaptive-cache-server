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

open Helpers
open LogPolicies
open MemoryPolicies
open CacheService

let mutable logPolicy = FactoryLogPolicy.Create Warning

let private cache = new CacheService()
let private cacheServiceToken = new CancellationTokenSource()
let private cacheService = async {
    let source = ConfigurationManager.AppSettings.Item("Server-Log")
    let address = ConfigurationManager.AppSettings.Item("IP-Address")
    let port = Int32.Parse(ConfigurationManager.AppSettings.Item("TCP-Port"))
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
            | ParseRegex "^(store:)(\s+)(.+)$" (value :: _) ->
                let request = async {return! cache.store (List.ofArray (ASCII.GetBytes(value)))}
                match Async.RunSynchronously(request) with
                | None ->
                    writer.WriteLine("error: ")
                    writer.Flush()
                | Some key ->
                    writer.WriteLine("key: " + key.ToString())
                    writer.Flush()
            | ParseRegex "^(remove:)(\s+)(\d+)$" (key :: _) ->
                cache.remove (Int32.Parse(key))
            | ParseRegex "^(search:)(\s+)(\d+)$" (key :: _) ->
                let request = async {return! cache.search (Int32.Parse(key))}
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
    let source = ConfigurationManager.AppSettings.Item("Console-Log")
    let command = Console.ReadLine()
    match command with
    | ParseRegex "^(memory:)(\s+)(high)$" _ ->
        cache.high
    | ParseRegex "^(memory:)(\s+)(low)(\s+)(\d+)$" (size :: _) ->
        cache.low (Int32.Parse(size))
    | ParseRegex "^(log:)(\s+)(information|warning|error)" (level :: _) ->
        match level with
        | "information" -> cache.log Information
        | "warning" -> cache.log Warning
        | "error" -> cache.log Error
        | _ -> ()
    | ParseRegex "^(protocol:)(\s+)(.+)$" (file :: _) ->
        ()
    | ParseRegex "^(config)$" _ ->
        for config in cache.config do
            Console.WriteLine(config)
    | ParseRegex "^(quit)$" _ ->
        cacheServiceToken.Cancel()
        running <- false
    | _ ->
        let log = [("Command \"" + command + "\" not found.", Warning)]
        logPolicy.log source log