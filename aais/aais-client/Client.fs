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

module Client

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text

open Helpers

let private address = "127.0.0.1"
let private port = 1234

let store words =
    let rec loop words (keys: int list) =
        match words with
        | [] -> keys
        | (head :: tail) ->
            use client = new TcpClient(address, port)
            use reader = new StreamReader(client.GetStream())
            use writer = new StreamWriter(client.GetStream())
            writer.WriteLine("store: " + head)
            writer.Flush()
            match reader.ReadLine() with
            | ParseRegex "^(key:)(\s+)(\d+)$" (key :: _) ->
                let keys = keys @ [Int32.Parse(key)]
                loop tail keys
            | ParseRegex "^(error:)(\s+)(.+)" (error :: _) ->
                Console.WriteLine(error)
                loop tail keys
            | _ ->
                loop tail keys
    loop words []

let keys = store ["pera"; "mela"; "fragola"; "banana"; "albicocca"]
for k in keys do
    Console.WriteLine(k)

let search keys =
    let rec loop keys (values: string list) =
        match keys with
        | [] -> values
        | (head :: tail) ->
            use client = new TcpClient(address, port)
            use reader = new StreamReader(client.GetStream())
            use writer = new StreamWriter(client.GetStream())
            writer.WriteLine("search: " + head.ToString())
            writer.Flush()
            match reader.ReadLine() with
            | ParseRegex "^(value:)(\s+)(.+)$" (value :: _) ->
                let values = values @ [value]
                loop tail values
            | ParseRegex "^(error:)(\s+)(.+)$" (error :: _) ->
                Console.WriteLine(error)
                loop tail values
            | _ ->
                loop tail values

    loop keys []

let values = search keys
for v in values do
    Console.WriteLine(v)

Console.ReadKey() |> ignore