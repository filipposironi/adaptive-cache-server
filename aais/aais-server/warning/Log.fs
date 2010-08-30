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

module Log

open System
open System.Diagnostics

let writeLogEntry source message level =
    let name = "Application"
    if not (EventLog.SourceExists(source)) then
        EventLog.CreateEventSource(source, name)
    EventLog.WriteEntry(source, message, level)

let description = "Log context is \"Warning Log\""
    
let log source messages =
    List.iter
        (fun (message, level) ->
            match level with
            | EventLogEntryType.Warning
            | EventLogEntryType.Error ->
                writeLogEntry source message level
            | _ -> ())
        messages