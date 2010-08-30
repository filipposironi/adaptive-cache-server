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

module Helpers

open System
open System.Configuration
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq

let (|ParseRegEx|_|) regex string =
    let matched = Regex(regex).Match(string)
    if matched.Success then
        Some (List.rev (List.filter (fun element -> element <> " ") (List.tail [for element in matched.Groups -> element.Value])))
    else
        None

type Protocol = Protocol of string * string * string * string * string * string * string * string * string
              | ProtocolError of string

let readProtocol id =
    let ToXName string = XName.Get(string)
    let protocolsDescription = ConfigurationManager.AppSettings.Item("Protocols")
    let protocols = XDocument.Load(protocolsDescription).Element(ToXName "protocols").Elements(ToXName "protocol")
    match Seq.tryFind (fun (p: XElement) -> p.Attribute(ToXName "id").Value = id) protocols with
    | None -> ProtocolError("Protocol \"" + id + "\" not found.")
    | Some(protocol) ->
        try
            let storeRegEx = protocol.Element(ToXName "store").Value
            let storeCommand = protocol.Element(ToXName "store").Attribute(ToXName "command").Value
            let removeRegEx = protocol.Element(ToXName "remove").Value
            let removeCommand = protocol.Element(ToXName "remove").Attribute(ToXName "command").Value
            let searchRegEx = protocol.Element(ToXName "search").Value
            let searchCommand = protocol.Element(ToXName "search").Attribute(ToXName "command").Value
            let keyRegEx = protocol.Element(ToXName "key").Value
            let valueRegEx = protocol.Element(ToXName "value").Value
            let errorRegEx = protocol.Element(ToXName "error").Value
            Protocol(storeRegEx, storeCommand, removeRegEx, removeCommand, searchRegEx, searchCommand, keyRegEx, valueRegEx, errorRegEx)
        with
        :? NullReferenceException ->
            ProtocolError("Protocol \"" + id + "\" format not supported.")