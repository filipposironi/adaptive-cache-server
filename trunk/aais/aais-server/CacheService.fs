#light

module CacheService

open System
open System.Collections
open System.Collections.Generic
open System.Diagnostics
open System.IO

open Helpers

let private logSource = "AdaptiveCacheService"
let private logName = "Application"

let private initialCache = new Map<int, bool * byte list>([])
let private initialCacheKeys = Map.ofList [for i in 0..(Convert.ToInt32 UInt16.MaxValue) -> (i, true)]
let private initialVolatileCacheSize = 0
let private initialVolatileCacheMaxSize = 10

type private Message = Store of byte list * AsyncReplyChannel<int>
                      | Remove of int
                      | Search of int * AsyncReplyChannel<byte list>
                      | Size of int
                      | Log of bool

type CacheService() =
    let MessageService = MailboxProcessor.Start(fun inbox ->
        let rec MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize =
            async { let! message = inbox.Receive()
                    match message with
                    | Store(value, outbox) ->
                        let key = Map.findKey (fun k v -> v = true) cacheKeys
                        let cacheKeys = Map.add key false cacheKeys
                        if volatileCacheSize + value.Length <= volatileCacheMaxSize then
                            let cache = Map.add key (true, value) cache
                            let volatileCacheSize = volatileCacheSize + value.Length
                            outbox.Reply key
                            return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                        else
                            let cache = Map.add key (false, []) cache
                            serializeCacheLine key (box value)
                            outbox.Reply key
                            return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                    | Remove(key) ->
                        if Map.containsKey key cache then
                            let (isVolatile, value) = Map.find key cache
                            if isVolatile then
                                let volatileCacheSize = volatileCacheSize - value.Length
                                let cache = Map.remove key cache
                                let cacheKeys = Map.add key true cacheKeys
                                return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                            else
                                File.Delete(key.ToString() + ".dat")
                                let cache = Map.remove key cache
                                let cacheKeys = Map.add key true cacheKeys
                                return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                        else
                            return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                    | Search(key, outbox) ->
                        try
                            let (isVolatile, value) = Map.find key cache
                            if isVolatile then
                                outbox.Reply value
                            else
                                let value = unbox<byte list> (deserializeCacheLine key)
                                outbox.Reply value
                        with
                        | :? KeyNotFoundException as e ->
                            if not (EventLog.SourceExists(logSource)) then
                                EventLog.CreateEventSource(logSource, logName)
                            EventLog.WriteEntry(logSource, e.Message, EventLogEntryType.Warning)
                            outbox.Reply []
                        return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                    | Size(newVolatileCacheMaxSize) ->
                        let rec increase (lCache: (int * (bool * byte list)) list) mCache volatileCacheSize =
                            if lCache.IsEmpty then
                                (mCache, volatileCacheSize)
                            else
                                let (key, (isVolatile, value)) = List.head lCache
                                if isVolatile && volatileCacheSize + value.Length > newVolatileCacheMaxSize then
                                    serializeCacheLine key (box value)
                                    increase (List.tail lCache) (Map.add key (false, []) mCache) volatileCacheSize
                                else
                                    increase (List.tail lCache) (Map.add key (isVolatile, value) mCache) (volatileCacheSize + value.Length)
                        let (cache, volatileCacheSize) = increase (Map.toList cache) (new Map<int, bool * byte list>([])) 0
                        return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                    | Log(log) ->
                        // TODO
                        return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize}
        MessageHandler initialCache initialCacheKeys initialVolatileCacheSize initialVolatileCacheMaxSize)

    member this.store value = MessageService.PostAndReply(fun inbox -> Store(value, inbox))
    member this.remove key = MessageService.Post(Remove(key))
    member this.search key = MessageService.PostAndReply(fun inbox -> Search(key, inbox))