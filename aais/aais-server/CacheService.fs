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

let private initialCache = new Map<int, bool * int * byte list>([])
let private initialCacheKeys = Map.ofList [for i in 0 .. (Convert.ToInt32 UInt16.MaxValue) -> (i, true)]
let private initialVolatileCacheSize = 0
let private initialVolatileCacheMaxSize = 1024

type private Message = Store of byte list * AsyncReplyChannel<int>
                     | Remove of int
                     | Search of int * AsyncReplyChannel<byte list>
                     | Size of int
                     | Log of bool

type CacheService() =
    let MessageService = MailboxProcessor.Start(fun inbox ->
        let rec MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize = async {
            let! message = inbox.Receive()
            match message with
            | Store(value, outbox) ->
                let key = Map.findKey (fun key value -> value = true) cacheKeys
                let cacheKeys = Map.add key false cacheKeys
                if volatileCacheSize + value.Length <= volatileCacheMaxSize then
                    let cache = Map.add key (true, value.Length, value) cache
                    let volatileCacheSize = volatileCacheSize + value.Length
                    outbox.Reply key
                    return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                else
                    let cache = Map.add key (false, value.Length, []) cache
                    serializeCacheLine key (box value)
                    outbox.Reply key
                    return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
            | Remove(key) ->
                if Map.containsKey key cache then
                    match Map.find key cache with
                    | (true, length, _) ->
                        let volatileCacheSize = volatileCacheSize - length
                        let cache = Map.remove key cache
                        let cacheKeys = Map.add key true cacheKeys
                        return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                    | (false, _, _) ->
                        File.Delete(key.ToString() + ".dat")
                        let cache = Map.remove key cache
                        let cacheKeys = Map.add key true cacheKeys
                        return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
                else
                    return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
            | Search(key, outbox) ->
                try
                    match Map.find key cache with
                    | (true, _, value) ->
                        outbox.Reply value
                    | (false, _, _) ->
                        outbox.Reply (unbox<byte list> (deserializeCacheLine key))
                with
                | :? KeyNotFoundException as e ->
                    if not (EventLog.SourceExists(logSource)) then
                        EventLog.CreateEventSource(logSource, logName)
                    EventLog.WriteEntry(logSource, e.Message, EventLogEntryType.Warning)
                    outbox.Reply []
                return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
            | Size(newVolatileCacheMaxSize) ->
                let decrease (cache: Map<int, bool * int * byte list>) =
                    let rec loop (lCache: (int * (bool * int * byte list)) list) mCache volatileCacheSize =
                        match lCache with
                        | [] -> (mCache, volatileCacheSize)
                        | (key, (_, length, value)) :: lCacheTail ->
                            if volatileCacheSize + length > newVolatileCacheMaxSize then
                                serializeCacheLine key (box value)
                                loop lCacheTail (Map.add key (false, length, []) mCache) volatileCacheSize
                            else
                                loop lCacheTail (Map.add key (true, length, value) mCache) (volatileCacheSize + length)
                    let (volatileCache, persistentCache) = Map.partition (fun _ (isVolatile, _, _) -> isVolatile) cache
                    loop (Map.toList volatileCache) persistentCache 0
                let increase (cache: Map<int, bool * int * byte list>) =
                    let rec loop (lCache: (int * (bool * int * byte list)) list) mCache volatileCacheSize =
                        match lCache with
                        | [] -> (mCache, volatileCacheSize)
                        | (key, (_, length, _)) :: lCacheTail ->
                            if volatileCacheSize + length < newVolatileCacheMaxSize then
                                let value = unbox<byte list> (deserializeCacheLine key)
                                File.Delete(key.ToString() + ".dat")
                                loop lCacheTail (Map.add key (true, length, value) mCache) (volatileCacheSize + length)
                            else
                                loop lCacheTail (Map.add key (false, length, []) mCache) volatileCacheSize
                    let (volatileCache, persistentCache) = Map.partition (fun _ (isVolatile, _, _) -> isVolatile) cache
                    loop (Map.toList persistentCache) volatileCache volatileCacheSize
                let update =
                    if newVolatileCacheMaxSize > volatileCacheMaxSize then
                        increase
                    else
                        decrease
                let (cache, volatileCacheSize) = update cache
                return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
            | Log(log) ->
                // TODO
                return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize}
        MessageHandler initialCache initialCacheKeys initialVolatileCacheSize initialVolatileCacheMaxSize)

    member this.store value = MessageService.PostAndReply(fun inbox -> Store(value, inbox))
    member this.remove key = MessageService.Post(Remove(key))
    member this.search key = MessageService.PostAndReply(fun inbox -> Search(key, inbox))
    member this.size size = MessageService.Post(Size(size))
    member this.log log = MessageService.Post(Log(log))