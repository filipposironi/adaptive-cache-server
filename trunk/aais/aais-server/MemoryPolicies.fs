#light

module MemoryPolicies

open System.Collections.Generic
open System.IO
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters.Binary

open Helpers
open LogPolicies

let serializeCacheLine key value =
    use file = new FileStream(key.ToString() + ".dat", FileMode.Create)
    let formatter = new BinaryFormatter()
    formatter.Serialize(file, value)
    file.Close()

let deserializeCacheLine key =
    use file = new FileStream(key.ToString() + ".dat", FileMode.Open)
    let formatter = new BinaryFormatter()
    let value = formatter.Deserialize(file)
    file.Close()
    value

type IMemoryPolicy =
    abstract member store: int -> byte list -> Map<int, (bool * int * byte list)> -> int -> (Map<int, (bool * int * byte list)> * int * (string * LogLevel) list)
    abstract member remove: int -> Map<int, (bool * int * byte list)> -> int -> (Map<int, (bool * int * byte list)> * int * (string * LogLevel) list)
    abstract member search: int -> Map<int, (bool * int * byte list)> -> (byte list * ((string * LogLevel) list))
    abstract member update: Map<int, (bool * int * byte list)> -> int -> float -> (Map<int, (bool * int * byte list)> * int)
    abstract member size: float

type HighMemoryPolicy() =
    member this.size = (this :> MemoryPolicy).size
    member this.store = (this :> MemoryPolicy).store
    member this.remove = (this :> MemoryPolicy).remove
    member this.search = (this :> MemoryPolicy).search
    member this.update = (this :> MemoryPolicy).update

    override this.ToString() = "Memory context is \"High Availability\""
    
    interface IMemoryPolicy with
        member this.size = infinity

        member this.store key value cache volatileCacheSize =
            let cache = Map.add key (true, value.Length, value) cache
            let volatileCacheSize = volatileCacheSize + value.Length
            let log = [("Key \"" + key.ToString() + "\" stored.", Information)]
            (cache, volatileCacheSize, log)
        
        member this.remove key cache volatileCacheSize =
            try
                let (_, length, _) = Map.find key cache
                let cache = Map.remove key cache
                let volatileCacheSize = volatileCacheSize - length
                let log = [("Key \"" + key.ToString() + "\" removed.", Information)]
                (cache, volatileCacheSize, log)
            with
            | :? KeyNotFoundException ->
                let log = [("Key \"" + key.ToString() + "\" not found.", Warning)]
                (cache, volatileCacheSize, log)

        member this.search key cache =
            try
                let (_, _, value) = Map.find key cache
                let log = [("Key \"" + key.ToString() + "\" retrieved.", Information)]
                (value, log)
            with
            | :? KeyNotFoundException ->
                let log = [("Key \"" + key.ToString() + "\" not found.", Error)]
                ([], log)

        member this.update cache volatileCacheSize oldVolatileCacheMaxSize =
            let rec loop (lCache: (int * (bool * int * byte list)) list) mCache volatileCacheSize =
                match lCache with
                | [] -> (mCache, volatileCacheSize)
                | (key, (_, length, _)) :: lCacheTail ->
                    let value = unbox<byte list> (deserializeCacheLine key)
                    File.Delete(key.ToString() + ".dat")
                    loop lCacheTail (Map.add key (true, length, value) mCache) (volatileCacheSize + length)
                
            let (volatileCache, persistentCache) = Map.partition (fun _ (isVolatile, _, _) -> isVolatile) cache
            loop (Map.toList persistentCache) volatileCache volatileCacheSize

type LowMemoryPolicy(size) =
    let volatileCacheMaxSize = size
    
    member this.size = (this :> MemoryPolicy).size
    member this.store = (this :> MemoryPolicy).store
    member this.remove = (this :> MemoryPolicy).remove
    member this.search = (this :> MemoryPolicy).search
    member this.update = (this :> MemoryPolicy).update

    override this.ToString() = "Memory context is \"Low Availability\""
    
    interface IMemoryPolicy with
        member this.size = float size

        member this.store key value cache volatileCacheSize =
            if volatileCacheSize + value.Length <= volatileCacheMaxSize then
                let cache = Map.add key (true, value.Length, value) cache
                let volatileCacheSize = volatileCacheSize + value.Length
                let log = [("Key \"" + key.ToString() + "\" stored.", Information)]
                (cache, volatileCacheSize, log)
            else
                let cache = Map.add key (false, value.Length, []) cache
                serializeCacheLine key (box value)
                let log = [("Key \"" + key.ToString() + "\" serialized.", Information)]
                (cache, volatileCacheSize, log)

        member this.remove key cache volatileCacheSize =
            try
                match Map.find key cache with
                | (true, length, _) ->
                    let cache = Map.remove key cache
                    let volatileCacheSize = volatileCacheSize - length
                    let log = [("Key \"" + key.ToString() + "\" removed.", Information)]
                    (cache, volatileCacheSize, log)
                | (false, _, _) ->
                    File.Delete(key.ToString() + ".dat")
                    let cache = Map.remove key cache
                    let log = [("Key \"" + key.ToString() + "\" deserialized and removed.", Information)]
                    (cache, volatileCacheSize, log)
            with
            | :? KeyNotFoundException ->
                let log = [("Key \"" + key.ToString() + "\" not found.", Warning)]
                (cache, volatileCacheSize, log)

        member this.search key cache =
            try
                match Map.find key cache with
                | (false, _, _) ->
                    let value = unbox<byte list> (deserializeCacheLine key)
                    let log = [("Key \"" + key.ToString() + "\" deserialized and retrieved.", Information)]
                    (value, log)
                | (true, _, value) ->
                    let log = [("Key \"" + key.ToString() + "\" retrieved.", Information)]
                    (value, log)
            with
            | :? KeyNotFoundException ->
                let log = [("Key \"" + key.ToString() + "\" not found.", Error)]
                ([], log)
        
        member this.update cache volatileCacheSize oldVolatileCacheMaxSize =
            let decrease (cache: Map<int, bool * int * byte list>) =
                let rec loop (lCache: (int * (bool * int * byte list)) list) mCache volatileCacheSize =
                    match lCache with
                    | [] -> (mCache, volatileCacheSize)
                    | (key, (_, length, value)) :: lCacheTail ->
                        if volatileCacheSize + length > volatileCacheMaxSize then
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
                        if volatileCacheSize + length < volatileCacheMaxSize then
                            let value = unbox<byte list> (deserializeCacheLine key)
                            File.Delete(key.ToString() + ".dat")
                            loop lCacheTail (Map.add key (true, length, value) mCache) (volatileCacheSize + length)
                        else
                            loop lCacheTail (Map.add key (false, length, []) mCache) volatileCacheSize
                
                let (volatileCache, persistentCache) = Map.partition (fun _ (isVolatile, _, _) -> isVolatile) cache
                loop (Map.toList persistentCache) volatileCache volatileCacheSize
            
            (if float volatileCacheMaxSize > oldVolatileCacheMaxSize then increase else decrease) cache
