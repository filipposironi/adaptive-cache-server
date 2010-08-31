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

module CacheService

open System
open System.Configuration
open System.Diagnostics
open System.IO
open System.Reflection

open Helpers
open MemoryPolicies

type private Message = Store of byte list * AsyncReplyChannel<int>
                     | Remove of int
                     | Search of int * AsyncReplyChannel<byte list>
                     | LowMemory of int
                     | HighMemory
                     | Log of EventLogEntryType
                     | Config of AsyncReplyChannel<string list>

type CacheService() =
    let source = ConfigurationManager.AppSettings.Item("Cache-Log")
    let timeout = 1000
    let messageService = MailboxProcessor.Start(fun inbox ->
        let rec loop cache keys volatileCacheSize (memoryPolicy: MemoryPolicy) logger currentLogDLL = async {
            let! message = inbox.Receive()
            match message with
            | Store(value, outbox) ->
                match keys with
                | [] ->
                    let log = [("Key not available.", EventLogEntryType.Warning)]
                    logger source log
                    return! loop cache keys volatileCacheSize memoryPolicy logger currentLogDLL
                | key :: keys ->
                    let (cache, volatileCacheSize, log) = (memoryPolicy.serialize >> memoryPolicy.store cache) (key, value, volatileCacheSize)
                    logger source log
                    outbox.Reply key
                    return! loop cache keys volatileCacheSize memoryPolicy logger currentLogDLL
            | Remove(key) ->
                let (cache, volatileCacheSize, log) = (memoryPolicy.deserialize >> memoryPolicy.remove volatileCacheSize) (key, cache)
                logger source log
                return! loop cache (key :: keys) volatileCacheSize memoryPolicy logger currentLogDLL
            | Search(key, outbox) ->
                let request cache key (memoryPolicy: MemoryPolicy) logger (outbox: AsyncReplyChannel<byte list>) = async {
                    match memoryPolicy.search key cache with
                    | ([], log) ->
                        logger source log
                    | (value, log) ->
                        logger source log
                        outbox.Reply value}
                Async.Start(request cache key memoryPolicy logger outbox)
                return! loop cache keys volatileCacheSize memoryPolicy logger currentLogDLL
            | LowMemory(volatileCacheMaxSize) ->
                let oldVolatileCacheMaxSize = memoryPolicy.size
                let memoryPolicy = new LowMemoryPolicy(volatileCacheMaxSize)
                let (cache, volatileCacheSize) = memoryPolicy.update cache volatileCacheSize oldVolatileCacheMaxSize
                let log = [("Memory context changed to \"Low Availability\".", EventLogEntryType.Information); ("Memory bound set to \"" + volatileCacheMaxSize.ToString() + "\".", EventLogEntryType.Information)]
                logger source log
                return! loop cache keys volatileCacheSize memoryPolicy logger currentLogDLL
            | HighMemory ->
                let oldVolatileCacheMaxSize = memoryPolicy.size
                let memoryPolicy = new HighMemoryPolicy()
                let (cache, volatileCacheSize) = memoryPolicy.update cache volatileCacheSize oldVolatileCacheMaxSize
                let log = [("Memory context changed to \"High Availability\".", EventLogEntryType.Information)]
                logger source log
                return! loop cache keys volatileCacheSize memoryPolicy logger currentLogDLL
            | Log(level) ->
                match level with
                | EventLogEntryType.Information ->
                    let logger source messages =
                        Assembly.LoadFrom("information.dll").GetType("Log").GetMethod("log").Invoke(null, [|source; messages|]) |> ignore
                    let log = [("Log context changed to \"Information\".", EventLogEntryType.Information)]
                    logger source log
                    return! loop cache keys volatileCacheSize memoryPolicy logger "information.dll"
                | EventLogEntryType.Warning ->
                    let logger source messages =
                        Assembly.LoadFrom("warning.dll").GetType("Log").GetMethod("log").Invoke(null, [|source; messages|]) |> ignore
                    let log = [("Log context changed to \"Warning\".", EventLogEntryType.Information)]
                    logger source log
                    return! loop cache keys volatileCacheSize memoryPolicy logger "warning.dll"
                | EventLogEntryType.Error ->
                    let logger source messages =
                        Assembly.LoadFrom("error.dll").GetType("Log").GetMethod("log").Invoke(null, [|source; messages|]) |> ignore
                    let log = [("Log context changed to \"Error\".", EventLogEntryType.Information)]
                    logger source log
                    return! loop cache keys volatileCacheSize memoryPolicy logger "error.dll"
                | _ -> ()
            | Config(outbox) ->
                let logDescription = Assembly.LoadFrom(currentLogDLL).GetType("Log").GetProperty("description").GetValue(null,null)
                outbox.Reply [memoryPolicy.ToString(); logDescription.ToString()]
                return! loop cache keys volatileCacheSize memoryPolicy logger currentLogDLL}
        let logger source messages =
            Assembly.LoadFrom("warning.dll").GetType("Log").GetMethod("log").Invoke(null, [|source; messages|]) |> ignore
        loop (new Map<int, bool * int * byte list>([])) [for i in 0 .. (Convert.ToInt32 UInt16.MaxValue) -> i] 0 (FactoryMemoryPolicy.Create()) logger "warning.dll")

    member this.store value = messageService.PostAndTryAsyncReply((fun inbox -> Store(value, inbox)), timeout)
    member this.remove key = messageService.Post(Remove(key))
    member this.search key = messageService.PostAndTryAsyncReply((fun inbox -> Search(key, inbox)), timeout)
    member this.low size = messageService.Post(LowMemory(size))
    member this.high = messageService.Post(HighMemory)
    member this.log level = messageService.Post(Log(level))
    member this.config = messageService.PostAndReply(fun inbox -> Config(inbox))