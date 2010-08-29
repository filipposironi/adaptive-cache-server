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
open System.Configuration
open System.IO
open System.Net.Sockets

open Helpers

let private address = ConfigurationManager.AppSettings.Item("IP-Address")
let private port = Int32.Parse(ConfigurationManager.AppSettings.Item("TCP-Port"))

let mutable running = true
while running do
    let command = Console.ReadLine()
    match command with
    | ParseRegex "^(store:)(\s+)(.+)$" (value :: _) ->
        use client = new TcpClient(address, port)
        use reader = new StreamReader(client.GetStream())
        use writer = new StreamWriter(client.GetStream())
        writer.WriteLine("store: " + value)
        writer.Flush()
        match reader.ReadLine() with
        | ParseRegex "^(key:)(\s+)(\d+)$" (key :: _) ->
            Console.WriteLine("key: " + key)
        | ParseRegex "^(error:)(\s+)(.+)" (error :: _) ->
            Console.WriteLine(error)
        | _ -> ()
    | ParseRegex "^(remove:)(\s+)(\d+)$" (key :: _) ->
        use client = new TcpClient(address, port)
        use reader = new StreamReader(client.GetStream())
        use writer = new StreamWriter(client.GetStream())
        writer.WriteLine("remove: " + key)
        writer.Flush()
    | ParseRegex "^(search:)(\s+)(\d+)$" (key :: _) ->
        use client = new TcpClient(address, port)
        use reader = new StreamReader(client.GetStream())
        use writer = new StreamWriter(client.GetStream())
        writer.WriteLine("search: " + key)
        writer.Flush()
        match reader.ReadLine() with
        | ParseRegex "^(value:)(\s+)(.+)$" (value :: _) ->
            Console.WriteLine("value: " + value)
        | ParseRegex "^(error:)(\s+)(.+)$" (error :: _) ->
            Console.WriteLine(error)
        | _ -> ()
    | ParseRegex "^(protocol:)(\s+)(.+)$" (id :: _) ->
        ()
    | ParseRegex "^(quit)$" _ ->
        running <- false
    | _ ->
        Console.WriteLine("Command \"" + command + "\" not supported.")