#light

module CacheService

open System
open System.IO

open Helpers
open MemoryPolicies
open LogPolicies

let private initialCache = new Map<int, bool * int * byte list>([])
let private initialCacheKeys = Map.ofList [for i in 0 .. (Convert.ToInt32 UInt16.MaxValue) -> (i, true)]
let private initialVolatileCacheSize = 0
let private initialVolatileCacheMaxSize = 1024

type private Message = Store of byte list * AsyncReplyChannel<int>
                     | Remove of int
                     | Search of int * AsyncReplyChannel<byte list>
                     | Size of int
                     | Log of string

type CacheService(memoryPolicy: MemoryPolicy, logPolicy: LogPolicy) =
    let source = "AdaptiveCacheService"
    let MessageService = MailboxProcessor.Start(fun inbox ->
        let rec MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize = async {
            let! message = inbox.Receive()
            match message with
            | Store(value, outbox) ->
                let key = Map.findKey (fun key value -> value = true) cacheKeys
                let cacheKeys = Map.add key false cacheKeys
                let (cache, volatileCacheSize, log) = memoryPolicy.store key value cache volatileCacheSize
                logPolicy.log source log
                outbox.Reply key
                return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
            | Remove(key) ->
                let (cache, volatileCacheSize, log) = memoryPolicy.remove key cache volatileCacheSize
                logPolicy.log source log
                return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
            | Search(key, outbox) ->
                let (value, log) = memoryPolicy.search key cache 
                logPolicy.log source log
                outbox.Reply value
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
                let (cache, volatileCacheSize) = (if newVolatileCacheMaxSize > volatileCacheMaxSize then increase else decrease) cache
                return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize
            | Log(level) ->
                // TODO
                return! MessageHandler cache cacheKeys volatileCacheSize volatileCacheMaxSize}
        MessageHandler initialCache initialCacheKeys initialVolatileCacheSize initialVolatileCacheMaxSize)

    member this.store value = MessageService.PostAndReply(fun inbox -> Store(value, inbox))
    member this.remove key = MessageService.Post(Remove(key))
    member this.search key = MessageService.PostAndReply(fun inbox -> Search(key, inbox))
    member this.size size = MessageService.Post(Size(size))
    member this.log log = MessageService.Post(Log(log))