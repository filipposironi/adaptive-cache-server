#light

module Client

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text

let private address = "127.0.0.1"
let private port = 1234
let private client = new TcpClient(address, port)
let private reader = new StreamReader(client.GetStream())
let private writer = new StreamWriter(client.GetStream())

let strings = ["pera"; "mela"; "fragola"; "banana"; "albicocca"]
for string in strings do
    writer.WriteLine("store")
    writer.Flush()
    writer.WriteLine(string)
    writer.Flush()
    Console.WriteLine(reader.ReadLine())