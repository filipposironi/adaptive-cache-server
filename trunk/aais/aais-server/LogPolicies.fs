#light

module LogPolicies

open System.Diagnostics

open Helpers

type LogPolicy =
    abstract log: string -> (string * LogLevel) list -> unit

type InformationLogPolicy() =
    member this.log = (this :> LogPolicy).log
    
    interface LogPolicy with
        member this.log source messages =
            for (message, level) in messages do
                match level with
                | Information ->
                    writeLogEntry source message level
                | _ -> ()

type WarningLogPolicy() =
    member this.log = (this :> LogPolicy).log
    
    interface LogPolicy with
        member this.log source messages =
            for (message, level) in messages do
                match level with
                | Information | Warning ->
                    writeLogEntry source message level
                | _ -> ()

type ErrorLogPolicy() =
    member this.log = (this :> LogPolicy).log
    
    interface LogPolicy with
        member this.log source messages =
            for (message, level) in messages do
                writeLogEntry source message level