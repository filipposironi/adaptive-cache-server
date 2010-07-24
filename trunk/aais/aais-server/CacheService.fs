#light

module Application.Server.CacheService

open System

type internal Message = Store of byte list * AsyncReplyChannel<int>
                      | Remove of int
                      | Search of int * AsyncReplyChannel<byte list>
                      | Size of int
                      | Log of bool
type CacheService() =
    let MessageService = MailboxProcessor.Start(fun inbox ->
        let rec MessageHandler (cache: Map<int, byte list>) (keys: Map<int, bool>) =
            async { let! message = inbox.Receive()
                    match message with
                    | Store(value, outbox) ->
                        let key = Map.findKey (fun k v -> v = true) keys
                        outbox.Reply key
                        return! MessageHandler (Map.add key value cache) (Map.add key false keys)
                    | Remove(key) ->
                        return! MessageHandler (Map.remove key cache) (Map.add key true keys)
                    | Search(key, outbox) ->
                        let value = Map.find key cache
                        outbox.Reply value
                        return! MessageHandler cache keys
                    | Size(size) ->
                        // TODO
                        return! MessageHandler cache keys
                    | Log(log) ->
                        // TODO
                        return! MessageHandler cache keys }
        MessageHandler (new Map<int, byte list>([])) (Map.ofList [for i in 0 .. (Convert.ToInt32 UInt16.MaxValue) -> (i, true)]))

    member this.store value = MessageService.PostAndReply(fun inbox -> Store(value, inbox))
    member this.remove key = MessageService.Post(Remove(key))
    member this.search key = MessageService.PostAndReply(fun inbox -> Search(key, inbox))