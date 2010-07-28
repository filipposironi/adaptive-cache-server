#light

module MemoryPolicies

open System.Collections.Generic
open System.IO

open Helpers

type MemoryPolicy =
    abstract member store: int -> byte list -> Map<int, (bool * int * byte list)> -> int -> (Map<int, (bool * int * byte list)> * int * (string * LogLevel) list)
    abstract member remove: int -> Map<int, (bool * int * byte list)> -> int -> (Map<int, (bool * int * byte list)> * int * (string * LogLevel) list)
    abstract member search: int -> Map<int, (bool * int * byte list)> -> (byte list * ((string * LogLevel) list))

type HighMemoryPolicy() =
    member this.store = (this :> MemoryPolicy).store
    member this.remove = (this :> MemoryPolicy).remove
    member this.search = (this :> MemoryPolicy).search
    
    interface MemoryPolicy with
        member this.store (key: int) (value: byte list) cache volatileCacheSize =
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

type LowMemoryPolicy(cacheMaxSize) =
    let volatileCacheMaxSize = cacheMaxSize
    
    member this.store = (this :> MemoryPolicy).store
    member this.remove = (this :> MemoryPolicy).remove
    member this.search = (this :> MemoryPolicy).search
    
    interface MemoryPolicy with
        member this.store key (value: byte list) cache volatileCacheSize =
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
                | (true, _, value) ->
                    let log = [("Key \"" + key.ToString() + "\" retrieved.", Information)]
                    (value, log)
                | (false, _, _) ->
                    let value = unbox<byte list> (deserializeCacheLine key)
                    let log = [("Key \"" + key.ToString() + "\" deserialized and retrieved.", Information)]
                    (value, log)
            with
            | :? KeyNotFoundException ->
                let log = [("Key \"" + key.ToString() + "\" not found.", Error)]
                ([], log)