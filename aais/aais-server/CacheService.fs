#light

module CacheService

open System
open System.Collections
open System.Collections.Generic
open System.IO
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters.Binary

type ICacheService =
    abstract member store : byte list -> int
    abstract member search : int -> byte list
    abstract member remove : int -> unit

type CacheService() =
    let cacheMaxSize = Convert.ToInt32(UInt16.MaxValue)
    let mutable keys = [for i in 0 .. cacheMaxSize -> (i, true)] |> Map.ofList
    let mutable volatileCache = new Map<int, byte list> ([])
    let mutable volatileCacheSize = 0
    let mutable volatileCacheMaxSize = 1024

    member this.getVolatileCacheMaxSize =
        volatileCacheMaxSize

    member this.setVolatileCacheMaxSize(size: int) =
        if size <= 0 then
            invalidArg "size" "size should be a positive interger value"
        volatileCacheMaxSize <- size
        this.update

    member this.store = (this :> ICacheService).store
    member this.search = (this :> ICacheService).search
    member this.remove = (this :> ICacheService).remove

    member this.update =
        if volatileCacheSize > volatileCacheMaxSize then
            let mutable size = 0
            for (k, v) in volatileCache |> Map.toSeq do
                if size + v.Length > volatileCacheMaxSize then
                    let file = new FileStream(k.ToString() + ".dat", FileMode.Create)
                    let formatter = new BinaryFormatter()
                    formatter.Serialize(file, v)
                    file.Close()
                    volatileCache <- volatileCache |> Map.add k []
                    volatileCacheSize <- volatileCacheSize - v.Length
                else
                    size <- size + v.Length
    
    interface ICacheService with
        member this.store(value: byte list) =
            let key = keys |> Map.findKey (fun k v -> v = true)
            keys <- keys |> Map.add key false
            if volatileCacheSize + value.Length <= volatileCacheMaxSize then
                volatileCache <- volatileCache |> Map.add key value
                volatileCacheSize <- volatileCacheSize + value.Length
            else
                volatileCache <- volatileCache |> Map.add key []
                let file = new FileStream(key.ToString() + ".dat", FileMode.Create)
                let formatter = new BinaryFormatter()
                formatter.Serialize(file, value)
                file.Close()
            key
        
        member this.search(key: int) =
            try
                if (volatileCache |> Map.find key = []) then
                    let file = new FileStream(key.ToString() + ".dat", FileMode.Open)
                    let formatter = new BinaryFormatter()
                    let value = (formatter.Deserialize(file) :?> byte list)
                    file.Close()
                    value
                else
                    Map.find key volatileCache
            with
                | :? KeyNotFoundException as e -> printfn "%s" (e.Message); []

        member this.remove(key: int) =
            try
                let value = volatileCache |> Map.find key
                if value.Length = 0 then
                    File.Delete(key.ToString() + ".dat")
                else
                    volatileCacheSize <- volatileCacheSize - value.Length
            with
                | :? KeyNotFoundException as e -> printfn "%s" (e.Message)
            volatileCache <- volatileCache |> Map.remove key
            keys <- keys |> Map.add key true
