﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dynamo.Models;
using Dynamo.Nodes;
using Microsoft.FSharp.Collections;
using Dynamo.Utilities;
using Dynamo.Revit;
using System.Xml;
using Autodesk.Revit.DB;

namespace Dynamo.Nodes
{
    public class dynFunctionWithRevit : dynFunction
    {
        internal ElementsContainer ElementsContainer = new ElementsContainer();

        protected internal dynFunctionWithRevit(IEnumerable<string> inputs, IEnumerable<string> outputs, FunctionDefinition functionDefinition)
            : base(inputs, outputs, functionDefinition)
        { }

        public dynFunctionWithRevit() { }

        public override FScheme.Value Evaluate(FSharpList<FScheme.Value> args)
        {
            dynRevitSettings.ElementsContainers.Push(ElementsContainer);
            var result = base.Evaluate(args);
            dynRevitSettings.ElementsContainers.Pop();
            return result;
        }

        protected override void SaveNode(XmlDocument xmlDoc, XmlElement dynEl, SaveContext context)
        {
            base.SaveNode(xmlDoc, dynEl, context);

            if (context == SaveContext.Copy)
                return;

            foreach (var node in ElementsContainer.Nodes)
            {
                var outEl = xmlDoc.CreateElement("InnerNode");
                outEl.SetAttribute("id", node.ToString());

                foreach (var run in ElementsContainer[node])
                {
                    var runEl = xmlDoc.CreateElement("Run");

                    foreach (var id in run)
                    {
                        Element e;
                        if (dynUtils.TryGetElement(id, typeof(object), out e))
                        {
                            var elementStore = xmlDoc.CreateElement("Element");
                            elementStore.InnerText = e.UniqueId;
                            runEl.AppendChild(elementStore);
                        }
                    }

                    outEl.AppendChild(runEl);
                }

                dynEl.AppendChild(outEl);
            }
        }

        protected override void LoadNode(XmlNode elNode)
        {
            base.LoadNode(elNode);

            ElementsContainer.Clear();

            foreach (XmlNode node in elNode.ChildNodes)
            {
                if (node.Name == "InnerNode")
                {
                    var nodeId = new Guid(node.Attributes["id"].Value);
                    var runs = ElementsContainer[nodeId];
                    runs.Clear();

                    foreach (XmlNode run in node.ChildNodes)
                    {
                        if (run.Name == "Run")
                        {
                            var runElements = new List<ElementId>();
                            runs.Add(runElements);

                            var query = from XmlNode element in run.ChildNodes
                                        where element.Name == "Element"
                                        select element.InnerText;

                            foreach (var eid in query) 
                            {
                                try
                                {
                                    runElements.Add(dynRevitSettings.Doc.Document.GetElement(eid).Id);
                                }
                                catch (NullReferenceException)
                                {
                                    DynamoLogger.Instance.Log("Element with UID \"" + eid + "\" not found in Document.");
                                }
                            }
                        }
                    }
                    var rNode = Definition.Workspace.Nodes.FirstOrDefault(x => x.GUID == nodeId) as dynRevitTransactionNode;
                    if (rNode != null)
                        rNode.RegisterAllElementsDeleteHook();
                }
            }
        }

        public override void Destroy()
        {
            IdlePromise.ExecuteOnIdle(
               delegate
               {
                   dynRevitSettings.Controller.InitTransaction();
                   try
                   {
                       ElementsContainer.DestroyAll();
                   }
                   catch (Exception ex)
                   {
                       DynamoLogger.Instance.Log(
                          "Error deleting elements: "
                          + ex.GetType().Name
                          + " -- " + ex.Message);
                   }
                   dynRevitSettings.Controller.EndTransaction();
                   WorkSpace.Modified();
               });
        }
    }
}
