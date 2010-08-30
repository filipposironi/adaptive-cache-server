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

module LogPolicies

open System
open System.Diagnostics

type LogLevel = Information
              | Warning
              | Error

let writeLogEntry source message level =
    let name = "Application"
    if not (EventLog.SourceExists(source)) then
        EventLog.CreateEventSource(source, name)
    match level with
    | Information ->
        EventLog.WriteEntry(source, message, EventLogEntryType.Information)        
    | Warning ->
        EventLog.WriteEntry(source, message, EventLogEntryType.Warning)
    | Error ->
        EventLog.WriteEntry(source, message, EventLogEntryType.Error)

type ILogPolicy =
    abstract log: string -> (string * LogLevel) list -> unit

type InformationLogPolicy() =
    member this.log = (this :> ILogPolicy).log

    override this.ToString() = "Log context is \"Information Log\""
    
    interface ILogPolicy with
        member this.log source messages =
            for (message, level) in messages do
                writeLogEntry source message level

type WarningLogPolicy() =
    member this.log = (this :> ILogPolicy).log

    override this.ToString() = "Log context is \"Warning Log\""
    
    interface ILogPolicy with
        member this.log source messages =
            for (message, level) in messages do
                match level with
                | Warning | Error ->
                    writeLogEntry source message level
                | _ -> ()

type ErrorLogPolicy() =
    member this.log = (this :> ILogPolicy).log

    override this.ToString() = "Log context is \"Error Log\""
    
    interface ILogPolicy with
        member this.log source messages =
            for (message, level) in messages do
                match level with
                | Error ->
                    writeLogEntry source message level
                | _ -> ()

type FactoryLogPolicy() =
    static member Create level =
        match level with
        | Information -> (new InformationLogPolicy() :> ILogPolicy)
        | Warning -> (new WarningLogPolicy() :> ILogPolicy)
        | Error -> (new ErrorLogPolicy() :> ILogPolicy)