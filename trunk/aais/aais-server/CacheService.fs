#light

module CacheService

open System
open System.Collections
open System.Collections.Generic
open System.IO
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters.Binary

type Message = Msg of string * int

type ICacheService =
    abstract member store: byte list -> int
    abstract member search: int -> byte list
    abstract member remove: int -> unit

type CacheService() =
    let cacheMaxSize = Convert.ToInt32 UInt16.MaxValue
    let enableLogMessages = ref false
    let keys = ref(Map.ofList [for i in 0 .. cacheMaxSize -> (i, true)])
    let volatileCache = ref(new Map<int, byte list>([]))
    let volatileCacheSize = ref 0
    let volatileCacheMaxSize = ref 1024
    let inbox = new MailboxProcessor<Message>(fun (inbox: MailboxProcessor<Message>) -> async {
        while true do
            let! message = inbox.Receive()
            match message with
            | Msg("size", size) ->
                if size <= 0 then
                    // TODO: avoid failing with an error
                    failwith "size less than or equal to zero"
                else
                    volatileCacheMaxSize := size
            | Msg("log", enable) ->
                if enable = 0 then
                    enableLogMessages := false
                else
                    enableLogMessages := true
            | Msg(_, _) -> ()
    })
    
    do
        // TODO: open log file
        inbox.Start()
       
    member this.getMailbox = inbox

    member this.store = (this :> ICacheService).store
    member this.search = (this :> ICacheService).search
    member this.remove = (this :> ICacheService).remove
    
    member this.update =
        if volatileCacheSize > volatileCacheMaxSize then
            let size = ref 0
            for (k, v) in Map.toSeq !volatileCache do
                if !size + v.Length > !volatileCacheMaxSize then
                    let file = new FileStream(k.ToString() + ".dat", FileMode.Create)
                    let formatter = new BinaryFormatter()
                    formatter.Serialize(file, v)
                    file.Close()
                    volatileCache := Map.add k [] !volatileCache
                    volatileCacheSize := !volatileCacheSize - v.Length
                else
                    size := !size + v.Length

    interface ICacheService with
        member this.store(value: byte list) =
            let key = Map.findKey (fun k v -> v = true) !keys
            keys := Map.add key false !keys
            if !volatileCacheSize + value.Length <= !volatileCacheMaxSize then
                volatileCache := Map.add key value !volatileCache
                volatileCacheSize := !volatileCacheSize + value.Length
            else
                volatileCache := Map.add key [] !volatileCache
                let file = new FileStream(key.ToString() + ".dat", FileMode.Create)
                let formatter = new BinaryFormatter()
                formatter.Serialize(file, value)
                file.Close()
            key
        
        member this.search(key: int) =
            try
                if Map.find key !volatileCache = [] then
                    let file = new FileStream(key.ToString() + ".dat", FileMode.Open)
                    let formatter = new BinaryFormatter()
                    let value =(formatter.Deserialize(file) :?> byte list)
                    file.Close()
                    value
                else
                    Map.find key !volatileCache
            with
            | :? KeyNotFoundException as e ->
                Console.WriteLine e.Message
                []

        member this.remove(key: int) =
            try
                let value = Map.find key !volatileCache
                if value.Length = 0 then
                    File.Delete(key.ToString() + ".dat")
                else
                    volatileCacheSize := !volatileCacheSize - value.Length
            with
                | :? KeyNotFoundException as e -> Console.WriteLine e.Message
            volatileCache := Map.remove key !volatileCache
            keys := Map.add key true !keys