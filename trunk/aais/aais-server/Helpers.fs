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

open System.Text.RegularExpressions

let (|ParseRegex|_|) regex string =
    let matched = Regex(regex).Match(string)
    if matched.Success then
        Some (List.rev (List.filter (fun element -> element <> " ") (List.tail [for element in matched.Groups -> element.Value])))
    else
        None