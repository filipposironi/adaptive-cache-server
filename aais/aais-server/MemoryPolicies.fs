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

[<AbstractClass>]
type MemoryPolicy() =
    abstract member size: float
    
    abstract member serialize: int * byte list * int -> (int * bool * byte list * int * int * (string * LogLevel) list)
    abstract member store: Map<int, (bool * int * byte list)> -> int * bool * byte list * int * int * (string * LogLevel) list -> (Map<int, (bool * int * byte list)> * int * (string * LogLevel) list)
    default this.store cache (key, isVolatile, value, length, volatileCacheSize, log) =
        let cache = Map.add key (isVolatile, length, value) cache
        let log = log @ [("Key \"" + key.ToString() + "\" stored.", Information)]
        (cache, volatileCacheSize, log)
    
    abstract member deserialize: int * Map<int, (bool * int * byte list)> -> (int * Map<int, (bool * int * byte list)> * (string * LogLevel) list)
    abstract member remove: int -> int * Map<int, (bool * int * byte list)> * (string * LogLevel) list -> (Map<int, (bool * int * byte list)> * int * (string * LogLevel) list)
    default this.remove volatileCacheSize (key, cache, log) =
        try
            let (_, _, value) = Map.find key cache
            let cache = Map.remove key cache
            let volatileCacheSize = volatileCacheSize - value.Length
            let log = log @ [("Key \"" + key.ToString() + "\" removed.", Information)]
            (cache, volatileCacheSize, log)
        with
        | :? KeyNotFoundException ->
            let log = [("Key \"" + key.ToString() + "\" not found.", Warning)]
            (cache, volatileCacheSize, log)
    
    abstract member search: int -> Map<int, (bool * int * byte list)> -> (byte list * ((string * LogLevel) list))
    abstract member update: Map<int, (bool * int * byte list)> -> int -> float -> (Map<int, (bool * int * byte list)> * int)

type HighMemoryPolicy() =
    inherit MemoryPolicy()

    override this.ToString() = "Memory context is \"High Availability\""
    
    override this.size = infinity

    override this.serialize (key, value, volatileCacheSize) =
        (key, true, value, value.Length, volatileCacheSize + value.Length, [])
    
    override this.deserialize (key, cache) =
        (key, cache, [])

    override this.search key cache =
        try
            let (_, _, value) = Map.find key cache
            let log = [("Key \"" + key.ToString() + "\" retrieved.", Information)]
            (value, log)
        with
        | :? KeyNotFoundException ->
            let log = [("Key \"" + key.ToString() + "\" not found.", Error)]
            ([], log)

    override this.update cache volatileCacheSize oldVolatileCacheMaxSize =
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
    inherit MemoryPolicy()

    let volatileCacheMaxSize = size
    
    override this.ToString() = "Memory context is \"Low Availability\""
    
    override this.size = float volatileCacheMaxSize

    override this.serialize (key, value, volatileCacheSize) =
        if volatileCacheSize + value.Length > volatileCacheMaxSize then
            serializeCacheLine key (box value)
            (key, false, [], value.Length, volatileCacheSize, [("Key \"" + key.ToString() + "\" serialized.", Information)])
        else
            (key, true, value, value.Length, volatileCacheSize + value.Length, [])

    override this.deserialize (key, cache) =
        try
            match Map.find key cache with
            | (false, _, _) ->
                File.Delete(key.ToString() + ".dat")
                (key, cache, [("Key \"" + key.ToString() + "\" deserialized.", Information)])
            | _ ->
                (key, cache, [])
        with
        | :? KeyNotFoundException ->
            (key, cache, [])

    override this.search key cache =
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
        
    override this.update cache volatileCacheSize oldVolatileCacheMaxSize =
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

type FactoryMemoryPolicy() =
    static member Create ?size =
        match size with
        | None -> (new HighMemoryPolicy() :> MemoryPolicy)
        | Some size -> (new LowMemoryPolicy(size) :> MemoryPolicy)