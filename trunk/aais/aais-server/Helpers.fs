#light

module Helpers

open System.IO
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters.Binary

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