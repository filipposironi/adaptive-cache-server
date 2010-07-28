#light

module Helpers

open System
open System.Diagnostics
open System.IO
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters.Binary
open System.Text.RegularExpressions

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

let serializeCacheLine key value =
    use file = new FileStream(key.ToString() + ".dat", FileMode.Create)
    let formatter = new BinaryFormatter()
    formatter.Serialize(file, value)
    file.Close()

let deserializeCacheLine key =
    use file = new FileStream(key.ToString() + ".dat", FileMode.Open)
    let formatter = new BinaryFormatter()
    let value = formatter.Deserialize(file)
    file.Close()
    value

let (|ParseRegex|_|) regex string =
    let matched = Regex(regex).Match(string)
    if matched.Success then
        Some (List.rev (List.filter (fun element -> element <> " ") (List.tail [for element in matched.Groups -> element.Value])))
    else
        None