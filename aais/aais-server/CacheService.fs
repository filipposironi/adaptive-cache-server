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
open System.IO

open Helpers
open LogPolicies
open MemoryPolicies

type private Message = Store of byte list * AsyncReplyChannel<int>
                     | Remove of int
                     | Search of int * AsyncReplyChannel<byte list>
                     | LowMemory of int
                     | HighMemory
                     | Log of LogLevel
                     | Config of AsyncReplyChannel<string list>

type CacheService() =
    let source = "AdaptiveCacheService"
    let timeout = 1000
    let messageService = MailboxProcessor.Start(fun inbox ->
        let rec loop cache keys volatileCacheSize (memoryPolicy: IMemoryPolicy) (logPolicy: ILogPolicy) = async {
            let! message = inbox.Receive()
            match message with
            | Store(value, outbox) ->
                match keys with
                | [] ->
                    let log = [("Key not available.", Warning)]
                    logPolicy.log source log
                    return! loop cache keys volatileCacheSize memoryPolicy logPolicy
                | key :: keys ->
                    let (cache, volatileCacheSize, log) = memoryPolicy.store key value cache volatileCacheSize
                    logPolicy.log source log
                    outbox.Reply key
                    return! loop cache keys volatileCacheSize memoryPolicy logPolicy
            | Remove(key) ->
                let (cache, volatileCacheSize, log) = memoryPolicy.remove key cache volatileCacheSize
                logPolicy.log source log
                return! loop cache (key :: keys) volatileCacheSize memoryPolicy logPolicy
            | Search(key, outbox) ->
                match memoryPolicy.search key cache with
                | ([], log) ->
                    logPolicy.log source log
                    return! loop cache keys volatileCacheSize memoryPolicy logPolicy
                | (value, log) ->
                    logPolicy.log source log
                    outbox.Reply value
                    return! loop cache keys volatileCacheSize memoryPolicy logPolicy
            | LowMemory(volatileCacheMaxSize) ->
                let oldVolatileCacheMaxSize = memoryPolicy.size
                let memoryPolicy = new LowMemoryPolicy(volatileCacheMaxSize)
                let (cache, volatileCacheSize) = memoryPolicy.update cache volatileCacheSize oldVolatileCacheMaxSize
                let log = [("Memory context changed to \"Low Availability\".", Information); ("Memory bound set to \"" + volatileCacheMaxSize.ToString() + "\".", Information)]
                logPolicy.log source log
                return! loop cache keys volatileCacheSize memoryPolicy logPolicy
            | HighMemory ->
                let oldVolatileCacheMaxSize = memoryPolicy.size
                let memoryPolicy = new HighMemoryPolicy()
                let (cache, volatileCacheSize) = memoryPolicy.update cache volatileCacheSize oldVolatileCacheMaxSize
                let log = [("Memory context changed to \"High Availability\".", Information)]
                logPolicy.log source log
                return! loop cache keys volatileCacheSize memoryPolicy logPolicy
            | Log(level) ->
                let logPolicy = FactoryLogPolicy.Create level
                match level with
                | Information ->
                    let log = [("Log context changed to \"Information\".", Information)]
                    logPolicy.log source log
                | Warning ->
                    let log = [("Log context changed to \"Warning\".", Information)]
                    logPolicy.log source log
                | Error ->
                    let log = [("Log context changed to \"Error\".", Information)]
                    logPolicy.log source log
                return! loop cache keys volatileCacheSize memoryPolicy logPolicy
            | Config(outbox) ->
                outbox.Reply [memoryPolicy.ToString(); logPolicy.ToString()]
                return! loop cache keys volatileCacheSize memoryPolicy logPolicy}
        loop (new Map<int, bool * int * byte list>([])) [for i in 0 .. (Convert.ToInt32 UInt16.MaxValue) -> i] 0 (FactoryMemoryPolicy.Create()) (FactoryLogPolicy.Create Warning))

    member this.store value = messageService.PostAndTryAsyncReply((fun inbox -> Store(value, inbox)), timeout)
    member this.remove key = messageService.Post(Remove(key))
    member this.search key = messageService.PostAndTryAsyncReply((fun inbox -> Search(key, inbox)), timeout)
    member this.low size = messageService.Post(LowMemory(size))
    member this.high = messageService.Post(HighMemory)
    member this.log level = messageService.Post(Log(level))
    member this.config = messageService.PostAndReply(fun inbox -> Config(inbox))