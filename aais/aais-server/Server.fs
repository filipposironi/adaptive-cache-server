#light

module Application.Server

open Application.Server.CacheService

let cache = new CacheService()
let key = cache.store [for i in 0 .. 9 -> byte i]