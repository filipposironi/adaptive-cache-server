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

let mutable storeRegEx = "^(store:)(\s+)(.+)$"
let mutable storeCommand = "store: "
let mutable removeRegEx = "^(remove:)(\s+)(\d+)$"
let mutable removeCommand = "remove: "
let mutable searchRegEx = "^(search:)(\s+)(\d+)$"
let mutable searchCommand = "search: "
let mutable keyRegEx = "^(key:)(\s+)(\d+)$"
let mutable valueRegEx = "^(value:)(\s+)(.+)$"
let mutable errorRegEx = "^(error:)(\s+)(.+)$"

match readProtocol (ConfigurationManager.AppSettings.Item("Default-Protocol")) with
| Protocol(newStoreRegEx, newStoreCommand, newRemoveRegEx, newRemoveCommand, newSearchRegEx, newSearchCommand, newKeyRegEx, newValueRegEx, newErrorRegEx) ->
    storeRegEx <- newStoreRegEx
    storeCommand <- newStoreCommand
    removeRegEx <- newRemoveRegEx
    removeCommand <- newRemoveCommand
    searchRegEx <- newSearchRegEx
    searchCommand <- newSearchCommand
    keyRegEx <- newKeyRegEx
    valueRegEx <- newValueRegEx
    errorRegEx <- newErrorRegEx
| ProtocolError(error) ->
    Console.WriteLine(error)

let mutable running = true
while running do
    Console.Write("$ ")
    let command = Console.ReadLine()
    match command with
    | ParseRegEx storeRegEx (value :: _) ->
        use client = new TcpClient(address, port)
        use reader = new StreamReader(client.GetStream())
        use writer = new StreamWriter(client.GetStream())
        writer.WriteLine(storeCommand + value)
        writer.Flush()
        match reader.ReadLine() with
        | ParseRegEx keyRegEx (key :: _) ->
            Console.WriteLine("key: " + key)
        | ParseRegEx errorRegEx (message :: _) ->
            Console.WriteLine("error: " + message)
        | message ->
            Console.WriteLine("error: \"" + message + "\" not recognized.")
    | ParseRegEx removeRegEx (key :: _) ->
        use client = new TcpClient(address, port)
        use reader = new StreamReader(client.GetStream())
        use writer = new StreamWriter(client.GetStream())
        writer.WriteLine(removeCommand + key)
        writer.Flush()
    | ParseRegEx searchRegEx (key :: _) ->
        use client = new TcpClient(address, port)
        use reader = new StreamReader(client.GetStream())
        use writer = new StreamWriter(client.GetStream())
        writer.WriteLine(searchCommand + key)
        writer.Flush()
        match reader.ReadLine() with
        | ParseRegEx valueRegEx (value :: _) ->
            Console.WriteLine("value: " + value)
        | ParseRegEx errorRegEx (message :: _) ->
            Console.WriteLine(message)
        | message ->
            Console.WriteLine("error: \"" + message + "\" not recognized.")
    | ParseRegEx "^(protocol:)(\s+)(.+)$" (id :: _) ->
        match readProtocol id with
        | Protocol(newStoreRegEx, newStoreCommand, newRemoveRegEx, newRemoveCommand, newSearchRegEx, newSearchCommand, newKeyRegEx, newValueRegEx, newErrorRegEx) ->
            storeRegEx <- newStoreRegEx
            storeCommand <- newStoreCommand
            removeRegEx <- newRemoveRegEx
            removeCommand <- newRemoveCommand
            searchRegEx <- newSearchRegEx
            searchCommand <- newSearchCommand
            keyRegEx <- newKeyRegEx
            valueRegEx <- newValueRegEx
            errorRegEx <- newErrorRegEx
        | ProtocolError(error) ->
            Console.WriteLine(error)
    | ParseRegEx "^(quit)$" _ ->
        running <- false
    | _ ->
        Console.WriteLine("Command \"" + command + "\" not supported.")