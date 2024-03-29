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

namespace NDjango
open System.Text
open System.Text.RegularExpressions
open System.Collections
open System.Collections.Generic
open System.Reflection

open NDjango.Interfaces
open Lexer
open ParserNodes
open Variables
open OutputHandling
open Utilities

module Expressions =
    
    /// Represents a single filter in the expression            
    type private Filter(context:ParsingContext, expression_token:TextToken, filter_match:Match) =
        let filter_token = expression_token.CreateToken(filter_match)
        let filter_name = filter_match.Groups.["filter_name"]
        let name_node = 
            new FilterNameNode (
                expression_token.CreateToken(filter_name), 
                context.Provider.Filters |> Map.to_list |> List.map (fun f -> fst f)
            )
            
        let args = filter_match.Groups.["arg"].Captures |> Seq.cast |> Seq.to_list 
                |> List.map 
                    (fun (c) ->
                        new Variable(context, expression_token.CreateToken(c))
                    )
                    
        let error, filter =
            try 
                match Map.tryFind filter_name.Value context.Provider.Filters with
                | None -> raise (SyntaxError (sprintf "filter %A could not be found" filter_name.Value))
                | Some filter ->
                    match filter, args with
                    | :? IFilter as f, [] when f.DefaultValue = null -> 
                         raise (SyntaxError ("filter requires argument, none provided"))
                    | _ -> new Error(-1, ""), Some filter
            with
            | :? SyntaxError as ex -> 
                if (context.Provider.Settings.[Constants.EXCEPTION_IF_ERROR] :?> bool)
                then
                    raise (SyntaxException(ex.Message, Text filter_token))
                else
                    new Error(2, ex.Message), None
            | _ -> rethrow()

        member x.Perform (context, input) =
            match filter with
            | None -> raise (SyntaxException(error.Message, Text filter_token))
            | Some f ->
                match f with
                | :? IFilter as std ->
                    let param = 
                        match args with
                        // we don't have to check for the presence of a default value here, as parse time
                        // check enforces that filters without defaults do not get called without parameters
                        | [] -> std.DefaultValue
                        | _ -> (args |> List.hd).Resolve context |> fst
                    std.PerformWithParam(input, param)
                | _ as simple -> simple.Perform input
            
        member x.elements = seq ([(name_node :> INode)] @ (args |> List.map (fun a -> (a:>INode))))
        member x.EscapeFilter = 
            match filter with
            | None -> raise (SyntaxException(error.Message, Text filter_token))
            | Some f ->
                match f with
                | :? NDjango.Filters.EscapeFilter -> true
                | _ -> false

        interface INode with
            member x.NodeType = NodeType.Filter
            member x.Position = filter_token.Location.Offset
            member x.Length = filter_token.Location.Length
            member x.Values = seq []
            member x.ErrorMessage = error
            member x.Description = ""
            member x.Nodes = 
                Map.of_list[(Constants.NODELIST_TAG_ELEMENTS, x.elements)] 
                    :> IDictionary<string, IEnumerable<INode>>

    /// Represents a filter expression 
    type FilterExpression (context:ParsingContext, expression: TextToken) =
        
        let expression_text = expression.Value

        let matches = Constants.filter_re.Matches expression_text
        
        let count, error, variable, filters =
            matches |> Seq.cast |> Seq.fold 
                (
                fun (offset, error, variable, filters) (mtch:Match) ->
                    try
                        // check if there are any gaps in the coverage of the string with the matches
                        if offset < mtch.Index 
                        then
                            raise (SyntaxError 
                                        (sprintf "Could not parse some characters %s|%s|%s" 
                                            expression_text.[..offset-1] 
                                            expression_text.[offset..mtch.Index] 
                                            expression_text.[mtch.Index..]
                                ))
                        else
                            match variable with
                            | None -> 
                                let var_match = mtch.Groups.["var"]
                                if var_match.Success then
                                    offset+mtch.Length, error, Some (new Variable(context, expression.CreateToken var_match)), filters
                                else
                                    raise (SyntaxError (sprintf "Could not find variable at the start of %s" expression_text))
                            | Some _ -> 
                                let token = expression.CreateToken(mtch) 
                                offset+mtch.Length, error, variable, filters @ 
                                    [new Filter(context, expression, mtch)]
                    with
                    | :? SyntaxError as ex ->
                        if (context.Provider.Settings.[Constants.EXCEPTION_IF_ERROR] :?> bool)
                        then
                            raise (SyntaxException(ex.Message, Text expression))
                        else
                            mtch.Index+mtch.Length, new Error(2, ex.Message), variable, filters
                    | _ -> rethrow()
                ) 
                (0, new Error(-1, ""), None, [])
        
        // check if we reached the end of the expression string        
        let error = 
            if count = expression_text.Length 
            then error
            else
                let message = 
                    sprintf "Could not parse some characters %s|%s" 
                        expression_text.[..count-1]
                        expression_text.[count..]
                if (context.Provider.Settings.[Constants.EXCEPTION_IF_ERROR] :?> bool)
                then
                    raise (SyntaxException(message, Text expression))
                else
                    new Error(2, message)

        /// resolves the filter against the given context. 
        /// the tuple returned consists of the value as the first item in the tuple
        /// and a boolean indicating whether the value needs escaping 
        /// if ignoreFailures is true, None is returned for failed expressions, otherwise an exception is thrown.
        member this.Resolve (context: IContext) ignoreFailures =
            let resolved_value =
                match variable with 
                | Some v -> 
                    try
                        let result = v.Resolve context
                        (Some (fst <| result), snd <| result)
                    with
                        | _ as exc -> 
                            if ignoreFailures then
                                (None, false)
                            else
                                rethrow()
                 | None ->
                    raise (SyntaxException(error.Message, Text expression))
            
            filters |> List.fold 
                (fun input filter ->
                    match fst input with
                    | None -> (None, false)
                    | Some value ->
                        if filter.EscapeFilter 
                        then (fst input, true)
                        else (Some (filter.Perform(context, value)), snd input)
                ) 
                resolved_value
            
        /// resolves the filter against the given context and 
        /// converts it to string taking into account escaping. 
        /// This method never fails, if the expression fails to resolve, 
        /// the method returns None
        member this.ResolveForOutput manager walker =
            let result, needsEscape = this.Resolve walker.context false
            match result with 
            | None -> None  // this results in no output from the expression
            | Some o -> 
                match o with 
                | :? INodeImpl as node -> Some (node.walk manager walker) // take output from the node
                | null -> None // this results in no output from the expression
                | _ as v ->
                    match if needsEscape then escape v else string v with
                    | "" -> None
                    | _ as s -> Some {walker with buffer = s}
            

            //TODO: django language spec allows 0 or 1 arguments to be passed to a filter, however the django implementation will handle any number
            //for filter, args in filters do
                
        interface INode with            
                     
            /// TagNode type = Expression
            member x.NodeType = NodeType.Expression 
            
            /// Position - the position of the first character of the expression
            member x.Position = expression.Location.Offset
            
            /// Length - the expression length
            member x.Length = expression.Location.Length

            /// List of available values empty
            member x.Values =  seq []
            
            /// error message associated with the node
            member x.ErrorMessage = error
            
            /// No description 
            member x.Description = ""
            
            /// node list consists of the variable node and the list of the filter nodes
            member x.Nodes =
                let list = 
                    (filters |> List.map (fun f -> f:>INode)) @
                    match variable with
                    | Some v -> [(v :> INode)] 
                    | None -> [] 
                new Map<string, IEnumerable<INode>>([]) 
                    |> Map.add Constants.NODELIST_TAG_ELEMENTS (list  :> IEnumerable<INode>) 
                        :> IDictionary<string, IEnumerable<INode>>


    // TODO: we still need to figure out the translation piece
    // python code
    //        if self.translate:
    //            return _(value)

