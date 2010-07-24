#light

module Application.Server.Main

open System

open Application.Server.CacheService

let cache = new CacheService()
let key1 = cache.store [for i in 0 .. 8 -> byte i]
printfn "%A" (cache.search key1)

let key2 = cache.store [for i in 0 .. 3 -> byte i]
printfn "%A" (cache.search key2)

Console.ReadKey() |> ignore