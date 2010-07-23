#light

module Main

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text

let address = "127.0.0.1"
let port = 1234
let client = new TcpClient(address, port)
let outStream = new StreamWriter(client.GetStream())
let inStream = new StreamReader(client.GetStream())

for i in 0..1023 do
    let command = "STORE"
    outStream.WriteLine command
    let value = i.ToString()
    outStream.WriteLine value
    outStream.Flush()
    let key = Int32.Parse(inStream.ReadLine())
    Console.WriteLine key

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

Console.WriteLine "hit any key to continue..."
Console.ReadKey() |> ignore