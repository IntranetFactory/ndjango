﻿/****************************************************************************
 * 
 *  NDjango Parser Copyright © 2009 Hill30 Inc
 *
 *  This file is part of the NDjango Designer.
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
 ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Text;
using System.Threading;
using NDjango.Interfaces;
using System.IO;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Language.Intellisense;

namespace NDjango.Designer.Parsing
{
    /// <summary>
    /// Manages a list of syntax nodes for a given buffer.
    /// </summary>
    class NodeProvider
    {
        // it can take some time for the parser to build the token list.
        // for now let us initialize it to an empty list
        private List<IDjangoSnapshot> nodes = new List<IDjangoSnapshot>();
        
        private object node_lock = new object();
        private IParser parser;
        private ITextBuffer buffer;
        private IVsOutputWindowPane djangoDiagnostics;
        string filePath;

        /// <summary>
        /// Creates a new node provider
        /// </summary>
        /// <param name="parser"></param>
        /// <param name="buffer">buffer to watch</param>
        public NodeProvider(IVsOutputWindowPane djangoDiagnostics, IParser parser, ITextBuffer buffer)
        {
            this.djangoDiagnostics = djangoDiagnostics;
            this.parser = parser;
            this.buffer = buffer;
            filePath = ((ITextDocument)buffer.Properties[typeof(ITextDocument)]).FilePath;
            rebuildNodes(buffer.CurrentSnapshot);
            buffer.Changed += new EventHandler<TextContentChangedEventArgs>(buffer_Changed);
        }

        public delegate void SnapshotEvent (SnapshotSpan snapshotSpan);

        private void buffer_Changed(object sender, TextContentChangedEventArgs e)
        {
            rebuildNodes(e.After);
        }

        /// <summary>
        /// Submits a request to rebuild the node list. The list is rebuilt in a separate thread
        /// After the rebuilt is completed, the NodesChanged event is fired
        /// </summary>
        /// <param name="snapshot"></param>
        private void rebuildNodes(ITextSnapshot snapshot)
        {
            ThreadPool.QueueUserWorkItem(rebuildNodesAsynch, snapshot);
        }

        /// <summary>
        /// This event is fired when an updated node list is ready to use
        /// </summary>
        public event SnapshotEvent NodesChanged;

        /// <summary>
        /// TextReader wrapper around text in the buffer
        /// </summary>
        class SnapshotReader : TextReader
        {
            ITextSnapshot snapshot;
            int pos = 0;
            public SnapshotReader(ITextSnapshot snapshot)
            {
                this.snapshot = snapshot;
            }

            public override int Read(char[] buffer, int index, int count)
            {
                int actual = snapshot.Length - pos;
                if (actual > count)
                    actual = count;
                if (actual > 0)
                    snapshot.ToCharArray(pos, actual).CopyTo(buffer, index);
                pos += actual;
                return actual;
            }
        }

        /// <summary>
        /// Builds a list of syntax nodes for a snapshot. This method is called on a separate thread
        /// </summary>
        private void rebuildNodesAsynch(object snapshotObject)
        {
            ITextSnapshot snapshot = (ITextSnapshot)snapshotObject;
            List<IDjangoSnapshot> nodes = parser.ParseTemplate(new SnapshotReader(snapshot))
                .ToList()
                    .ConvertAll<IDjangoSnapshot>
                        (node => new NodeSnapshot(snapshot, (INode)node));
            lock (node_lock)
            {
                this.nodes = nodes;
            }
            ShowDiagnostics();
            if (NodesChanged != null)
                NodesChanged(new SnapshotSpan(snapshot, 0, snapshot.Length));
        }

        internal void ShowDiagnostics()
        {
            List<IDjangoSnapshot> nodes;
            lock (node_lock)
            {
                nodes = this.nodes;
            }
            djangoDiagnostics.Clear();
            nodes.ForEach(node=>node.ShowDiagnostics(djangoDiagnostics, filePath));
            djangoDiagnostics.FlushToTaskList();
        }

        /// <summary>
        /// Returns a list of nodes in the specified snapshot span
        /// </summary>
        /// <param name="snapshotSpan"></param>
        /// <returns></returns>
        internal List<IDjangoSnapshot> GetNodes(ITextSnapshot snapshot)
        {
            List<IDjangoSnapshot> nodes;
            lock (node_lock)
            {
                nodes = this.nodes;

                // just in case if while the tokens list was being rebuilt
                // another modification was made
                if (nodes.Count > 0 && this.nodes[0].SnapshotSpan.Snapshot != snapshot)
                    this.nodes.ForEach(token => token.TranslateTo(snapshot));
            }

            return nodes;
        }

        /// <summary>
        /// Returns a list of nodes in the specified snapshot span
        /// </summary>
        /// <param name="snapshotSpan"></param>
        /// <returns></returns>
        private List<IDjangoSnapshot> GetNodes(SnapshotSpan snapshotSpan, Predicate<IDjangoSnapshot> predicate)
        {
            return GetNodes(snapshotSpan, GetNodes(snapshotSpan.Snapshot))
                .FindAll(predicate);
        }

        internal List<IDjangoSnapshot> GetNodes(SnapshotSpan snapshotSpan)
        {
            return GetNodes(snapshotSpan, node => node.ContentType != ContentType.Context);
        }



        /// <summary>
        /// Traverses the node tree building a flat list of nodes intersecting with the span
        /// </summary>
        /// <param name="snapshotSpan"></param>
        /// <param name="nodes"></param>
        /// <returns></returns>
        private List<IDjangoSnapshot> GetNodes(SnapshotSpan snapshotSpan, IEnumerable<IDjangoSnapshot> nodes)
        {
            List<IDjangoSnapshot> result = new List<IDjangoSnapshot>();
            foreach (IDjangoSnapshot node in nodes)
            {
                if (node.SnapshotSpan.IntersectsWith(snapshotSpan) || node.ExtensionSpan.IntersectsWith(snapshotSpan))
                    result.Add(node);
                result.AddRange(GetNodes(snapshotSpan, node.Children));
            }
            return result;
        }
        
        /// <summary>
        /// Returns a list of django syntax nodes based on the point in the text buffer
        /// </summary>
        /// <param name="point">point identifiying the desired node</param>
        /// <returns></returns>
        private List<IDjangoSnapshot> GetNodes(SnapshotPoint point, Predicate<IDjangoSnapshot> predicate)
        {
            return GetNodes(new SnapshotSpan(point.Snapshot, point.Position, 0), predicate);
        }

        internal List<IDjangoSnapshot> GetNodes(SnapshotPoint point)
        {
            return GetNodes(new SnapshotSpan(point.Snapshot, point.Position, 0)
                , node => node.ContentType != ContentType.Context);
        }

        internal List<CompletionSet> GetCompletions(SnapshotPoint point, string trigger)
        {
            List<IDjangoSnapshot> nodes = GetNodes(point, node=>true).FindAll(node => node.Values.Count() > 0);
            List<CompletionSet> result = new List<CompletionSet>();
            if (trigger.Length > 0)
                switch (trigger)
                {
                    case "{%":
                        CreateCompletionSet(nodes, result, point, 
                                node => node.ContentType == ContentType.Context,
                                "% ",
                                " %}");
                        break;
                    case ":":
                    case "|":
                        CreateCompletionSet(nodes, result, point, 
                                node => node.ContentType == ContentType.FilterName,
                                trigger,
                                "");
                        break;
                    default:
                        if (Char.IsLetterOrDigit(trigger[0]))
                            CreateCompletionSet(nodes, result, point, 
                                    node => node.ContentType != ContentType.Context,
                                    "",
                                    "");
                        break;
                }
            return result;
        }

        private void CreateCompletionSet(
                List<IDjangoSnapshot> nodes,
                List<CompletionSet> sets,
                SnapshotPoint point, 
                Predicate<IDjangoSnapshot> selector, 
                string prefix, 
                string suffix
            )
        {
            var node = nodes.FindLast(selector);
            if (node == null)
                return;
            Span span = new Span(point.Position, 0);
            if (node.SnapshotSpan.IntersectsWith(span))
                span = node.SnapshotSpan.Span;
            var applicableTo = point.Snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive);
            sets.Add(new CompletionSet(
                "NDjango Completions",
                applicableTo,
                CompletionsForNode(node.Values, prefix, suffix),
                null
                ));
        }

        private IEnumerable<Completion> CompletionsForNode(IEnumerable<string> values, string prefix, string suffix)
        {
            foreach (string value in values)
                yield return new Completion(value, prefix + value + suffix, value);
        }
    }
}
