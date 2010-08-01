#light

module LogPolicies

open System
open System.Diagnostics

open Helpers

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
        Console.WriteLine(message)
    | Error ->
        EventLog.WriteEntry(source, message, EventLogEntryType.Error)
        Console.WriteLine(message)


type ILogPolicy =
    abstract log: string -> (string * LogLevel) list -> unit

type InformationLogPolicy() =
    member this.log = (this :> LogPolicy).log

    override this.ToString() = "Log context is \"Information Log\""
    
    interface ILogPolicy with
        member this.log source messages =
            for (message, level) in messages do
                writeLogEntry source message level

type WarningLogPolicy() =
    member this.log = (this :> LogPolicy).log

    override this.ToString() = "Log context is \"Warning Log\""
    
    interface ILogPolicy with
        member this.log source messages =
            for (message, level) in messages do
                match level with
                | Warning | Error ->
                    writeLogEntry source message level
                | _ -> ()

type ErrorLogPolicy() =
    member this.log = (this :> LogPolicy).log

    override this.ToString() = "Log context is \"Error Log\""
    
    interface ILogPolicy with
        member this.log source messages =
            for (message, level) in messages do
                match level with
                | Error -> writeLogEntry source message level
                | _ -> ()

type FactoryLogPolicy() =
    static member create level =
        match level with
        | Information -> (new InformationLogPolicy() :> LogPolicy)
        | Warning -> (new WarningLogPolicy() :> LogPolicy)
        | Error -> (new ErrorLogPolicy() :> LogPolicy)
