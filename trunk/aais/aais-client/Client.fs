#light

module Main

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text

let client = new TcpClient("127.0.0.1", 1234)
let outStream = new StreamWriter(client.GetStream())
let inStream = new StreamReader(client.GetStream())

(*
outStream.WriteLine("Hello, World!")
outStream.Flush()
printfn "%s" (inStream.ReadLine())
*)

for i in 0..1023 do
    let command = "STORE"
    outStream.WriteLine(command)
    let value = i.ToString()
    outStream.WriteLine(value)
    outStream.Flush()
    let key = Int32.Parse(inStream.ReadLine())
    printfn "%d" key

(*
outStream.WriteLine("SEARCH " + key.ToString())
outStream.Flush()
message <- inStream.ReadLine()
let list = message.Split([|' '|]).[1]
printf "%A" list

outStream.WriteLine("REMOVE " + key.ToString())
outStream.Flush()
message <- inStream.ReadLine()
*)

printfn "Hit any key to continue..."
do (Console.ReadKey()) |> ignore
