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

type Protocol = Protocol of string * string * string * string * string * string
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
            let removeRegEx = protocol.Element(ToXName "remove").Value
            let searchRegEx = protocol.Element(ToXName "search").Value
            let keyCommand = protocol.Element(ToXName "key").Attribute(ToXName "command").Value
            let valueCommand = protocol.Element(ToXName "value").Attribute(ToXName "command").Value
            let errorCommand = protocol.Element(ToXName "error").Attribute(ToXName "command").Value
            Protocol(storeRegEx, removeRegEx, searchRegEx, keyCommand, valueCommand, errorCommand)
        with
        :? NullReferenceException ->
            ProtocolError("Protocol \"" + id + "\" format not supported.")