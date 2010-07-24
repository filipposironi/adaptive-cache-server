#light

module Application.Server.Main

open System

open Application.Server.CacheService

let cache = new CacheService()
let key = cache.store [for i in 0 .. 9 -> byte i]
let value = cache.search key
printfn "%A" value

cache.search 2 |> ignore

Console.ReadKey() |> ignore