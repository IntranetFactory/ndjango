﻿using EnvDTE;
using EnvDTE80;

namespace Microsoft.SymbolBrowser.ObjectLists
{
    public class ModelReferenceList : ResultList
    {
        public ModelReferenceList(string text, string fName)
            : base(text, fName, 5, LibraryNodeType.Classes)
        {
            // class list
        }

        protected override bool IsExpandable
        {
            get { return true; }
        }
        public override bool CanGoToSource
        {
            get
            {
                return true; // models can go to source
            }
        }
        protected override void GotoSource(VisualStudio.Shell.Interop.VSOBJGOTOSRCTYPE gotoType)
        {
            //foreach(SymbolBrowserPackage.DTE2Obj.Solution.Projects.Count
            
        }
    }
}
