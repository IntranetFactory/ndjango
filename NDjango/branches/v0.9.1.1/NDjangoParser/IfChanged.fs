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


namespace NDjango.Tags

open NDjango.Lexer
open NDjango.Interfaces
open NDjango.ParserNodes
open NDjango.Expressions
open NDjango.OutputHandling

module internal IfChanged =

/// Check if a value has changed from the last iteration of a loop.
/// 
/// The 'ifchanged' block tag is used within a loop. It has two possible uses.
/// 
/// Checks its own rendered contents against its previous state and only displays the content if it has changed. For example, this displays a list of days, only displaying the month if it changes:
/// 
/// <h1>Archive for {{ year }}</h1>
/// 
/// {% for date in days %}
///     {% ifchanged %}<h3>{{ date|date:"F" }}</h3>{% endifchanged %}
///     <a href="{{ date|date:"M/d"|lower }}/">{{ date|date:"j" }}</a>
/// {% endfor %}
/// If given a variable, check whether that variable has changed. For example, the following shows the date every time it changes, but only shows the hour if both the hour and the date has changed:
/// 
/// {% for date in days %}
///     {% ifchanged date.date %} {{ date.date }} {% endifchanged %}
///     {% ifchanged date.hour date.date %}
///         {{ date.hour }}
///     {% endifchanged %}
/// {% endfor %}

    type Tag() =
        interface ITag with
            member this.Perform token provider tokens =
                let nodes_ifchanged, remaining = (provider :?> IParser).Parse (Some token) tokens ["else"; "endifchanged"]
                let nodes_ifsame, remaining =
                    match nodes_ifchanged.[nodes_ifchanged.Length-1].Token with
                    | NDjango.Lexer.Block b -> 
                        if b.Verb = "else" then
                            (provider :?> IParser).Parse (Some token) remaining ["endifchanged"]
                        else
                            [], remaining
                    | _ -> [], remaining

                let createWalker manager =
                    match token.Args |> List.map (fun var -> new Variable(provider, Block token, var)) with
                    | [] ->
                        fun walker ->
                            let reader = 
                                new NDjango.ASTWalker.Reader (manager, {walker with parent=None; nodes=nodes_ifchanged; context=walker.context})
                            let newValue = reader.ReadToEnd() :> obj
                            match walker.context.tryfind("$oldValue") with
                            | Some o when o = newValue -> {walker with nodes = List.append nodes_ifsame walker.nodes}
                            | _ -> {walker with buffer= string newValue; context=walker.context.add("$oldValue", newValue)}
                    | _ as vars ->
                        fun walker ->
                            let newValues = vars |> List.map (fun var -> (var.Resolve walker.context |> fst)) 
                            
                            // this function returns true if there is a mismatch
                            let matchValues (oldVals:obj) newVals =
                                match oldVals with
                                | :? List<obj> as list when (list |> List.length) = (newVals |> List.length) 
                                    -> List.exists2 (<>) list newVals
                                | _ -> true
                                
                            match walker.context.tryfind("$oldValue") with
                            | Some o when not <| matchValues o newValues -> {walker with nodes = List.append nodes_ifsame walker.nodes}
                            | _ -> {walker with nodes = List.append nodes_ifchanged walker.nodes; context=walker.context.add("$oldValue", (newValues :> obj))}
                (({
                    new TagNode(provider, token)
                    with
                        override this.walk manager walker =
                            createWalker manager walker 
                                   
                        override this.Nodes 
                            with get() =
                                base.Nodes 
                                    |> Map.add (NDjango.Constants.NODELIST_IFTAG_IFTRUE) (nodes_ifchanged |> Seq.map (fun node -> (node :?> INode)))
                                    |> Map.add (NDjango.Constants.NODELIST_IFTAG_IFFALSE) (nodes_ifsame |> Seq.map (fun node -> (node :?> INode)))

                    } :> INodeImpl), remaining)
