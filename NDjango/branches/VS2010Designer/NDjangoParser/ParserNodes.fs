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

open System.Collections.Generic
open OutputHandling
open Lexer
open NDjango.Interfaces

module internal ParserNodes =

    /// Django construct bracket type
    type private BracketType = 
        /// Open bracket
        |Open
        /// Close bracket
        |Close

    /// Base class for all Django syntax nodes.
    [<AbstractClass>]
    type Node(token: Token) =

        /// Methods/Properties for the INodeImpl interface
        /// Indicates whether this node must be the first non-text node in the template
        abstract member must_be_first: bool
        default x.must_be_first = false
        
        /// The token that defined the node
        member x.Token with get() = token

        /// Advances the walker as a part of the tag rendering process
        abstract member walk: ITemplateManager -> Walker -> Walker
        default  x.walk manager walker = walker

        /// List of child nodes used by the tags with a single list of child nodes e.g. spaceless, with or escape
        /// this list is also used by the extends tag to build a list of tags substitution
        abstract member nodelist: INodeImpl list
        default x.nodelist with get() = []
        
        /// Methods/Properties for the INode interface
        /// Node type - only nodes of NodeType.Construct are important for rendering.
        /// The rest of them are used for diagnostic
        abstract member node_type: NodeType

        /// A dictionary of all lists of child nodes
        /// by iterating through the dictionary a complete list of all elements and child nodes can be retrieved
        abstract member Nodes: Map<string, IEnumerable<INode>>
        default x.Nodes 
            with get() =
                new Map<string, IEnumerable<INode>>([]) 
                    |> Map.add Constants.NODELIST_TAG_CHILDREN (x.nodelist |> Seq.map (fun node -> (node :?> INode)))
                    |> Map.add Constants.NODELIST_TAG_ELEMENTS (x.elements :> IEnumerable<INode>)
        
        /// A list of nodes representing django construct elements including construct markers, tag name , variable, etc.
        abstract member elements: INode list
        default x.elements 
            with get() = 
                [
                    (new ConstructBracketNode(token, Open) :> INode); 
                    (new ConstructBracketNode(token, Close) :> INode)
                ]
        
        /// A list of all values allowed for the node, i.e. for the tag name node a list of all registered tags
        abstract member Values: IEnumerable<string>
        default x.Values = seq []
            
        /// Error message represented by this node
        abstract member ErrorMessage: Error
        default x.ErrorMessage = new Error(-1,"")
            
        /// Description to be shown for this node
        abstract member Description: string
        default x.Description = ""

        interface INode with

            member x.NodeType = x.node_type
            /// Position - the position of the first character of the token 
            member x.Position = token.Position
            /// Length - length of the token
            member x.Length = token.Length
            member x.Values = x.Values
            member x.ErrorMessage = x.ErrorMessage
            member x.Description = x.Description
            member x.Nodes = x.Nodes :> IDictionary<string, IEnumerable<INode>>

        interface INodeImpl with
            member x.must_be_first = x.must_be_first
            member x.Token = x.Token
            member x.walk manager walker = x.walk manager walker
            
    /// Node representing a django construct bracket
    and private ConstructBracketNode(token: Token, bracketType: BracketType) =

        interface INode with
            
            /// TagNode type = marker
            member x.NodeType = NodeType.Marker 
            
            /// Position - start position for the open bracket, endposition - 2 for the close bracket 
            member x.Position = 
                match bracketType with
                | Open -> token.Position
                | Close -> token.Position + token.Length - 2
            
            /// Length of the marker = 2
            member x.Length = 2

            /// No values allowed for the node
            member x.Values = seq []
            
            /// No message associated with the node
            member x.ErrorMessage = new Error(-1,"")
            
            /// No description 
            member x.Description = ""
            
            /// node lists are empty
            member x.Nodes = Map.empty :> IDictionary<string, IEnumerable<INode>>
   
    /// Value list node is a node carrying a list of values which will be used by code completion
    /// it can be used either directly or through several node classes inherited from the Value list node
    type ValueListNode(nodeType, token: Token, values)  =
            
        interface INode with
            member x.NodeType = nodeType 
            member x.Position = token.Position
            member x.Length = token.Length
            member x.Values = values
            /// No message associated with the node
            member x.ErrorMessage = new Error(-1,"")
            /// No description 
            member x.Description = ""
            /// node list is empty
            member x.Nodes = Map.empty :> IDictionary<string, IEnumerable<INode>>
            
    /// a node representing a tag name. carries alist of valid tag names 
    type TagNameNode (context: ParsingContext, token) =
        inherit ValueListNode
            (
                NodeType.TagName, 
                token,
                context.Tags
            )
            
    /// a node representing a variable - i.e. loop variable in the For tag        
    type VariableNode (token:TextToken, values:string list) =
        inherit ValueListNode
            (
                NodeType.Variable, 
                Text token,
                values
            )
    /// a node representing a keyword - i.e. on/off values for the autoescape tag
    type KeywordNode (token:TextToken, values:string list) =
        inherit ValueListNode
            (
                NodeType.Keyword, 
                Text token,
                values
            )
        /// a node representing a keyword - i.e. on/off values for the autoescape tag
    type ReferenceNode (token:TextToken, values:string list) =
        inherit ValueListNode
            (
                NodeType.Reference, 
                Text token,
                values
            )       
            
            
    /// a node representing a filter name
    type FilterNameNode (token:TextToken, values:string list) =
        inherit ValueListNode
            (
                NodeType.FilterName, 
                Text token,
                values
            )
            
    /// For tags decorated with this attribute the string given as a parmeter for the attribute
    /// will be shown in the tooltip for the tag            
    type DescriptionAttribute(description: string) = 
        inherit System.Attribute()
        
        member x.Description = description

    /// Base class for all syntax nodes representing django tags
    type TagNode(context: ParsingContext, token: BlockToken) =
        inherit Node(Block token)

        /// NodeType = Tag
        override x.node_type = NodeType.Construct   
        
        /// Add TagName node to the list of elements
        override x.elements =
            (new TagNameNode(context, Text token.Verb) :> INode) :: base.elements
            
        override x.Description =
            match context.Provider.Tags.TryFind(token.Verb.RawText) with
            | None -> ""
            | Some tag -> 
                let attrs = tag.GetType().GetCustomAttributes(typeof<DescriptionAttribute>, false)
                attrs |> Array.fold (fun text attr -> text + (attr :?> DescriptionAttribute).Description ) ""
            
    /// Error nodes
    type ErrorNode(context: ParsingContext, token: Token, error: Error) =
        inherit Node(token)

        // in some cases (like an empty tag) we need this for proper colorization
        // if the colorization is already there it does not hurt
        override x.node_type = NodeType.Construct   
        
        override x.ErrorMessage = error

        /// Walking an error node throws an error
        override x.walk manager walker = 
            raise (SyntaxException(error.Message, token))
            
    type TagSyntaxError(message: string, pattern:INode list) =
        inherit SyntaxError(message)
        
        member x.Pattern = pattern
