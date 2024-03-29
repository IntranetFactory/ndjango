﻿(****************************************************************************
 * 
 *  NDjango Parser Copyright © 2009 Hill30 Inc
 *
 *  This file is part of the NDjango Parser.
 *
 *  The NDjango Parser is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU Lesser General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  The NDjango Parser is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with NDjango Parser.  If not, see <http://www.gnu.org/licenses/>.
 *  
 ***************************************************************************)

#light

namespace NDjango

open NDjango.Interfaces

module internal ASTWalker =
    type Reader(walker: Walker) =
        inherit System.IO.TextReader()
        
        let mutable walker = walker
        let buffer = Array.create 4096 ' '
        
        let rec getChar() = 
            if walker.bufferIndex >= walker.buffer.Length then
                match walker.nodes with
                | [] ->
                    match walker.parent with 
                    | Some w -> 
                        walker <- w
                        getChar() 
                    | None -> -1 // we are done - nothing more to walk
                | node :: nodes ->
                    walker <- node.walk {walker with nodes = nodes; buffer=""; bufferIndex = 0}
                    getChar()
            else
                Char.code buffer.[walker.bufferIndex]

        let read (buffer: char[]) index count = 
            let mutable transferred = 0;
            while getChar() <> -1 && transferred < count do
                if walker.bufferIndex < walker.buffer.Length then
                    let mutable index = walker.bufferIndex
                    while index < walker.buffer.Length && transferred < count do
                        buffer.[transferred] <- walker.buffer.[index]
                        transferred <- transferred+1
                        index <- index+1
                    walker <- {walker with bufferIndex = index}
                // should never happen after a call to getChar                    
                else raise (System.Exception("should never happen after a call to getChar"))
            transferred

        let rec read_to_end (buffers:System.Text.StringBuilder) = 
            match read buffer 0 buffer.Length with
            | 0 -> buffers
            | _ as l ->
                if l = buffer.Length then 
                    buffers.Append(new System.String(buffer)) |> ignore
                    read_to_end buffers
                else 
                    buffers.Append(new System.String(buffer), 0, l) |> ignore
                    read_to_end buffers

        override this.Peek() =
            getChar()
            
        override this.Read() =
            let result = getChar()
            if result <> -1 then
                if walker.bufferIndex < walker.buffer.Length then
                    walker <- {walker with bufferIndex = walker.bufferIndex+1}
                // should never happen after a call to getChar                    
                else raise (System.Exception("should never happen after a call to getChar"))
            result
        
        override this.Read(buffer: char[], index: int, count: int) = read buffer index count

        override this.ReadToEnd() = 
         read_to_end (new System.Text.StringBuilder()) |> string
