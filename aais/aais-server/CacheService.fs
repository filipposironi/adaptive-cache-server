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

type CacheService() =
    let source = "AdaptiveCacheService"
    let messageService = MailboxProcessor.Start(fun inbox ->
        let rec loop cache cacheKeys volatileCacheSize (memoryPolicy: MemoryPolicy) (logPolicy: LogPolicy) = async {
            let! message = inbox.Receive()
            match message with
            | Store(value, outbox) ->
                let key = Map.findKey (fun key value -> value = true) cacheKeys
                let cacheKeys = Map.add key false cacheKeys
                let (cache, volatileCacheSize, log) = memoryPolicy.store key value cache volatileCacheSize
                logPolicy.log source log
                outbox.Reply key
                return! loop cache cacheKeys volatileCacheSize memoryPolicy logPolicy
            | Remove(key) ->
                let (cache, volatileCacheSize, log) = memoryPolicy.remove key cache volatileCacheSize
                logPolicy.log source log
                return! loop cache cacheKeys volatileCacheSize memoryPolicy logPolicy
            | Search(key, outbox) ->
                let (value, log) = memoryPolicy.search key cache 
                logPolicy.log source log
                outbox.Reply value
                return! loop cache cacheKeys volatileCacheSize memoryPolicy logPolicy
            | LowMemory(volatileCacheMaxSize) ->
                let oldVolatileCacheMaxSize = memoryPolicy.size
                let memoryPolicy = new LowMemoryPolicy(volatileCacheMaxSize)
                let (cache, volatileCacheSize) = memoryPolicy.update cache volatileCacheSize oldVolatileCacheMaxSize
                let log = [("Memory context changed to \"Low Availability\".", Information); ("Memory bound set to \"" + volatileCacheMaxSize.ToString() + "\".", Information)]
                logPolicy.log source log
                return! loop cache cacheKeys volatileCacheSize memoryPolicy logPolicy
            | HighMemory ->
                let oldVolatileCacheMaxSize = memoryPolicy.size
                let memoryPolicy = new HighMemoryPolicy()
                let (cache, volatileCacheSize) = memoryPolicy.update cache volatileCacheSize oldVolatileCacheMaxSize
                let log = [("Memory context changed to \"High Availability\".", Information)]
                logPolicy.log source log
                return! loop cache cacheKeys volatileCacheSize memoryPolicy logPolicy
            | Log(level) ->
                let logPolicy = FactoryLogPolicy.create level
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
                return! loop cache cacheKeys volatileCacheSize memoryPolicy logPolicy}
        loop (new Map<int, bool * int * byte list>([])) (Map.ofList [for i in 0 .. (Convert.ToInt32 UInt16.MaxValue) -> (i, true)]) 0 (new HighMemoryPolicy()) (new ErrorLogPolicy()))

    member this.store value = messageService.PostAndReply(fun inbox -> Store(value, inbox))
    member this.remove key = messageService.Post(Remove(key))
    member this.search key = messageService.PostAndReply(fun inbox -> Search(key, inbox))
    member this.low size = messageService.Post(LowMemory(size))
    member this.high = messageService.Post(HighMemory)
    member this.log level = messageService.Post(Log(level))