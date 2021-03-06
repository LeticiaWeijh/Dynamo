﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Windows;
using System.Windows.Forms;
using System.Xml;
using Dynamo.Nodes;
using Dynamo.Utilities;
using Dynamo.ViewModels;
using Dynamo.Selection;
using Microsoft.Practices.Prism;
using NUnit.Framework;

namespace Dynamo.Models
{
    public delegate void FunctionNamePromptRequestHandler(object sender, FunctionNamePromptEventArgs e);
    public delegate void CleanupHandler(object sender, EventArgs e);

    public class FunctionNamePromptEventArgs : EventArgs
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public bool Success { get; set; }

        public FunctionNamePromptEventArgs()
        {
            Name = "";
            Category = "";
            Description = "";
        }
    }

    /// <summary>
    /// The Dynamo model.
    /// </summary>
    public class DynamoModel:dynModelBase
    {
        public event EventHandler RequestLayoutUpdate;
        public virtual void OnRequestLayoutUpdate(object sender, EventArgs e)
        {
            if (RequestLayoutUpdate != null)
                RequestLayoutUpdate(this, e);
        }

        public event FunctionNamePromptRequestHandler RequestsFunctionNamePrompt;
        public virtual void OnRequestsFunctionNamePrompt(Object sender, FunctionNamePromptEventArgs e)
        {
            if (RequestsFunctionNamePrompt != null)
            {
                RequestsFunctionNamePrompt(this, e);
            }
        }

        private ObservableCollection<dynWorkspaceModel> _workSpaces = new ObservableCollection<dynWorkspaceModel>();
        private ObservableCollection<dynWorkspaceModel> _hiddenWorkspaces = new ObservableCollection<dynWorkspaceModel>();
        public string UnlockLoadPath { get; set; }
        private dynWorkspaceModel _cspace;
        internal string editName = "";

        public dynWorkspaceModel CurrentSpace
        {
            get { return _cspace; }
            internal set
            {
                if (_cspace != null)
                    _cspace.IsCurrentSpace = false;
                _cspace = value;
                _cspace.IsCurrentSpace = true;
                RaisePropertyChanged("CurrentSpace");
            }
        }

        public dynWorkspaceModel HomeSpace { get; protected set; }

        /// <summary>
        ///     The collection of visible workspaces in Dynamo
        /// </summary>
        public ObservableCollection<dynWorkspaceModel> Workspaces
        {
            get { return _workSpaces; }
            set 
            { 
                _workSpaces = value;
            }
        }

        public List<dynNodeModel> Nodes
        {
            get { return CurrentSpace.Nodes.ToList(); }
        }

        public void HideWorkspace(dynWorkspaceModel workspace)
        {
            this.CurrentSpace = _workSpaces[0];  // typically the home workspace
            _workSpaces.Remove(workspace);
            _hiddenWorkspaces.Add(workspace);
        }

        /// <summary>
        /// Replace the home workspace with a new 
        /// workspace. Only valid if the home workspace is already
        /// defined (usually by calling AddHomeWorkspace).
        /// </summary>
        public void NewHomeWorkspace()
        {
            if (this.Workspaces.Count > 0 && this.HomeSpace != null)
            {
                //var homeIndex = this._workSpaces.IndexOf(this.HomeSpace);
                //var newHomespace = new HomeWorkspace();
                //this.Workspaces[0] = newHomespace;
                //this.HomeSpace = newHomespace;
                //this.CurrentSpace = newHomespace;

                this.AddHomeWorkspace();
                _cspace = this.HomeSpace;
                this.CurrentSpace = this.HomeSpace;
                this.Workspaces.RemoveAt(1);
            }
        }

        /// <summary>
        /// Add a workspace to the dynamo model.
        /// </summary>
        /// <param name="workspace"></param>
        public void AddHomeWorkspace()
        {
            var workspace = new HomeWorkspace()
            {
                WatchChanges = true
            };
            HomeSpace = workspace;
            _workSpaces.Insert(0, workspace); // to front
        }

        /// <summary>
        /// Remove a workspace from the dynamo model.
        /// </summary>
        /// <param name="workspace"></param>
        public void RemoveWorkspace(dynWorkspaceModel workspace)
        {
            _workSpaces.Remove(workspace);
        }

        public static bool RunEnabled { get; set; }

        public static bool RunInDebug { get; set; }

        /// <summary>
        /// All nodes in all workspaces. 
        /// </summary>
        public IEnumerable<dynNodeModel> AllNodes
        {
            get
            {
                return Workspaces.Aggregate((IEnumerable<dynNodeModel>)new List<dynNodeModel>(), (a, x) => a.Concat(x.Nodes))
                    .Concat(dynSettings.Controller.CustomNodeManager.GetLoadedDefinitions().Aggregate(
                        (IEnumerable<dynNodeModel>)new List<dynNodeModel>(),
                        (a, x) => a.Concat(x.Workspace.Nodes)
                        )
                    );
            }
        }

        /// <summary>
        /// An event triggered when the workspace is being cleaned.
        /// </summary>
        public event CleanupHandler CleaningUp;

        public virtual void OnCleanup(EventArgs e)
        {
            if (CleaningUp != null)
                CleaningUp(this, e);
        }

        /// <summary>
        /// Present the open dialogue and open the workspace that is selected.
        /// </summary>
        /// <param name="parameter"></param>
        public void ShowOpenDialogAndOpenResult(object parameter)
        {
            var vm = dynSettings.Controller.DynamoViewModel;

            if (vm.Model.HomeSpace.HasUnsavedChanges && !vm.AskUserToSaveWorkspaceOrCancel(vm.Model.HomeSpace))
            {
                return;
            }

            FileDialog _fileDialog = null;

            if (_fileDialog == null)
            {
                _fileDialog = new OpenFileDialog()
                {
                    Filter = "Dynamo Definitions (*.dyn; *.dyf)|*.dyn;*.dyf|All files (*.*)|*.*",
                    Title = "Open Dynamo Definition..."
                };
            }

            // if you've got the current space path, use it as the inital dir
            if (!string.IsNullOrEmpty(vm.Model.CurrentSpace.FilePath))
            {
                var fi = new FileInfo(vm.Model.CurrentSpace.FilePath);
                _fileDialog.InitialDirectory = fi.DirectoryName;
            }
            else // use the samples directory, if it exists
            {
                Assembly dynamoAssembly = Assembly.GetExecutingAssembly();
                string location = Path.GetDirectoryName(dynamoAssembly.Location);
                string path = Path.Combine(location, "samples");

                if (Directory.Exists(path))
                {
                    _fileDialog.InitialDirectory = path;
                }
            }

            if (_fileDialog.ShowDialog() == DialogResult.OK)
            {
                //if (OpenCommand.CanExecute(_fileDialog.FileName))
                //    OpenCommand.Execute(_fileDialog.FileName);
                if (CanOpen(_fileDialog.FileName))
                    Open(_fileDialog.FileName);
            }
        }

        internal bool CanShowOpenDialogAndOpenResultCommand(object parameter)
        {
            return true;
        }

        /// <summary>
        /// Open a definition or workspace.
        /// </summary>
        /// <param name="parameters">The path the the file.</param>
        public void Open(object parameters)
        {
            string xmlPath = parameters as string;

            dynSettings.Controller.IsUILocked = true;

            if (!OpenDefinition(xmlPath))
            {
                DynamoLogger.Instance.Log("Workbench could not be opened.");

                if (CanWriteToLog(null))
                {
                    WriteToLog("Workbench could not be opened.");
                    WriteToLog(xmlPath);
                }
            }

            dynSettings.Controller.IsUILocked = false;

            //clear the clipboard to avoid copying between dyns
            dynSettings.Controller.ClipBoard.Clear();
        }

        internal bool CanOpen(object parameters)
        {
            if (string.IsNullOrEmpty(parameters.ToString()))
                return false;
            return true;
        }

        internal void PostUIActivation(object parameter)
        {

            DynamoLoader.LoadCustomNodes();

            DynamoLogger.Instance.Log("Welcome to Dynamo!");

            if (UnlockLoadPath != null && !OpenWorkspace(UnlockLoadPath))
            {
                DynamoLogger.Instance.Log("Workbench could not be opened.");

                if (CanWriteToLog(null))
                {
                    WriteToLog("Workbench could not be opened.");
                    WriteToLog(UnlockLoadPath);
                }
            }

            UnlockLoadPath = null;
            dynSettings.Controller.IsUILocked = false;
            HomeSpace.OnDisplayed();

        }

        internal bool CanDoPostUIActivation(object parameter)
        {
            return true;
        }

        public class WorkspaceHeader
        {
            private WorkspaceHeader()
            {
                
            }

            public static WorkspaceHeader FromPath(string path)
            {
                try
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.Load(path);

                    string funName = null;
                    double cx = 0;
                    double cy = 0;
                    double zoom = 1.0;
                    string id = "";

                    // load the header
                    foreach (XmlNode node in xmlDoc.GetElementsByTagName("dynWorkspace"))
                    {
                        foreach (XmlAttribute att in node.Attributes)
                        {
                            if (att.Name.Equals("X"))
                                cx = double.Parse(att.Value, CultureInfo.InvariantCulture);
                            else if (att.Name.Equals("Y"))
                                cy = double.Parse(att.Value, CultureInfo.InvariantCulture);
                            else if (att.Name.Equals("zoom"))
                                zoom = double.Parse(att.Value, CultureInfo.InvariantCulture);
                            else if (att.Name.Equals("Name"))
                                funName = att.Value;
                            else if (att.Name.Equals("ID"))
                            {
                                id = att.Value;
                            }
                        }
                    }

                    // we have a dyf and it lacks an ID field, we need to assign it
                    // a deterministic guid based on its name.  By doing it deterministically,
                    // files remain compatible
                    if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(funName) && funName != "Home")
                    {
                        id = GuidUtility.Create(GuidUtility.UrlNamespace, funName).ToString();
                    }


                    return new WorkspaceHeader() { ID = id, Name = funName, X = cx, Y = cy, Zoom = zoom, FilePath = path };


                }
                catch (Exception ex)
                {
                    DynamoLogger.Instance.Log("There was an error opening the workbench.");
                    DynamoLogger.Instance.Log(ex);
                    Debug.WriteLine(ex.Message + ":" + ex.StackTrace);

                    if (dynSettings.Controller.Testing)
                        Assert.Fail(ex.Message);

                    return null;
                }
            }

            public double X { get; set; }
            public double Y { get; set; }
            public double Zoom { get; set; }
            public string Name { get; set; }
            public string ID { get; set; }
            public string FilePath { get; set; }

            public bool IsCustomNodeWorkspace()
            {
                return !String.IsNullOrEmpty(ID);
            }
        }

        internal void OpenCustomNodeAndFocus( WorkspaceHeader workspaceHeader )
        {
            // load custom node
            var manager = dynSettings.Controller.CustomNodeManager;
            var info = manager.AddFileToPath(workspaceHeader.FilePath);
            var funcDef = manager.GetFunctionDefinition(info.Guid);
            var ws = funcDef.Workspace;
            ws.Zoom = workspaceHeader.Zoom;

            if (!this.Workspaces.Contains(ws))
            {
                this.Workspaces.Add(ws);
            }

            var vm = dynSettings.Controller.DynamoViewModel.Workspaces.First(x => x.Model == ws);
            vm.OnCurrentOffsetChanged(this, new PointEventArgs(new Point(workspaceHeader.X, workspaceHeader.Y)));

            this.CurrentSpace = ws;
        }   
        
        internal bool OpenDefinition( string xmlPath )
        {

            var workspaceInfo = WorkspaceHeader.FromPath(xmlPath);

            if (workspaceInfo == null)
            {
                return false;
            }

            if (workspaceInfo.IsCustomNodeWorkspace())
            {
                OpenCustomNodeAndFocus(workspaceInfo);
                return true;
            }
            else
            {
                //View the home workspace, then open the bench file
                if (!dynSettings.Controller.DynamoViewModel.ViewingHomespace)
                    ViewHomeWorkspace();

                dynSettings.Controller.CustomNodeManager.AddDirectoryToSearchPath(Path.GetDirectoryName(xmlPath));
                dynSettings.Controller.CustomNodeManager.UpdateSearchPath();

                return OpenWorkspace(xmlPath);
            }

        }

        /// <summary>
        ///     Change the currently visible workspace to the home workspace
        /// </summary>
        /// <param name="symbol">The function definition for the custom node workspace to be viewed</param>
        internal void ViewHomeWorkspace()
        {
            CurrentSpace = HomeSpace;
            CurrentSpace.OnDisplayed();
        }

        /// <summary>
        ///     Create a node from a type object in a given workspace.
        /// </summary>
        /// <param name="elementType"> The Type object from which the node can be activated </param>
        /// <param name="nickName"> A nickname for the node.  If null, the nickName is loaded from the NodeNameAttribute of the node </param>
        /// <param name="guid"> The unique identifier for the node in the workspace. </param>
        /// <param name="x"> The x coordinate where the dynNodeView will be placed </param>
        /// <param name="y"> The x coordinate where the dynNodeView will be placed</param>
        /// <returns> The newly instantiate dynNode</returns>
        public dynNodeModel CreateInstanceAndAddNodeToWorkspace(Type elementType, string nickName, Guid guid,
            double x, double y, dynWorkspaceModel ws, bool isVisible = true, bool isUpstreamVisible = true)    //Visibility vis = Visibility.Visible)
        {
            try
            {
                dynNodeModel node = CreateNodeInstance(elementType, nickName, guid);

                ws.Nodes.Add(node);
                node.WorkSpace = ws;

                node.X = x;
                node.Y = y;

                node.IsVisible = isVisible;
                node.IsUpstreamVisible = isUpstreamVisible;

                return node;
            }
            catch (Exception e)
            {
                DynamoLogger.Instance.Log("Could not create an instance of the selected type: " + elementType);
                DynamoLogger.Instance.Log(e);
                return null;
            }
        }


        /// <summary>
        ///     Create a build-in node from a type object in a given workspace.
        /// </summary>
        /// <param name="elementType"> The Type object from which the node can be activated </param>
        /// <param name="nickName"> A nickname for the node.  If null, the nickName is loaded from the NodeNameAttribute of the node </param>
        /// <param name="guid"> The unique identifier for the node in the workspace. </param>
        /// <returns> The newly instantiated dynNode</returns>
        public dynNodeModel CreateNodeInstance(Type elementType, string nickName, Guid guid)
        {
            var node = (dynNodeModel)Activator.CreateInstance(elementType);

            if (!string.IsNullOrEmpty(nickName))
            {
                node.NickName = nickName;
            }
            else
            {
                var elNameAttrib =
                    node.GetType().GetCustomAttributes(typeof(NodeNameAttribute), true)[0] as NodeNameAttribute;
                if (elNameAttrib != null)
                {
                    node.NickName = elNameAttrib.Name;
                }
            }

            node.GUID = guid;

            //string name = nodeUI.NickName;
            return node;
        }

        internal void CleanWorkbench()
        {
            DynamoLogger.Instance.Log("Clearing workflow...");

            //Copy locally
            List<dynNodeModel> elements = Nodes.ToList();

            foreach (dynNodeModel el in elements)
            {
                el.DisableReporting();
                //try
                //{
                //    el.Destroy();
                //}
                //catch
                //{
                //}
            }

            foreach (dynNodeModel el in elements)
            {
                foreach (dynPortModel p in el.InPorts)
                {
                    for (int i = p.Connectors.Count - 1; i >= 0; i--)
                        p.Connectors[i].NotifyConnectedPortsOfDeletion();
                }
                foreach (dynPortModel port in el.OutPorts)
                {
                    for (int i = port.Connectors.Count - 1; i >= 0; i--)
                        port.Connectors[i].NotifyConnectedPortsOfDeletion();
                }
            }

            CurrentSpace.Connectors.Clear();
            CurrentSpace.Nodes.Clear();
            CurrentSpace.Notes.Clear();
        }

        /// <summary>
        /// Open a workspace from a path.
        /// </summary>
        /// <param name="xmlPath">The path to the workspace.</param>
        /// <returns></returns>
        public bool OpenWorkspace(string xmlPath)
        {
            DynamoLogger.Instance.Log("Opening home workspace " + xmlPath + "...");

            CleanWorkbench();

            //clear the renderables
            dynSettings.Controller.RenderDescriptions.Clear();
            dynSettings.Controller.OnRequestsRedraw(dynSettings.Controller, EventArgs.Empty);

            Stopwatch sw = new Stopwatch();

            try
            {
                #region read xml file

                sw.Start();

                var xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlPath);

                TimeSpan previousElapsed = sw.Elapsed;
                DynamoLogger.Instance.Log(string.Format("{0} elapsed for loading xml.", sw.Elapsed));

                double cx = 0;
                double cy = 0;
                double zoom = 1.0;

                foreach (XmlNode node in xmlDoc.GetElementsByTagName("dynWorkspace"))
                {
                    foreach (XmlAttribute att in node.Attributes)
                    {
                        if (att.Name.Equals("X"))
                        {
                            cx = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        }
                        else if (att.Name.Equals("Y"))
                        {
                            cy = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        }
                        else if (att.Name.Equals("zoom"))
                        {
                            zoom = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        }
                    }
                }

                //set the zoom and offsets and trigger events
                //to get the view to position iteself
                CurrentSpace.X = cx;
                CurrentSpace.Y = cy;
                CurrentSpace.Zoom = zoom;

                var vm = dynSettings.Controller.DynamoViewModel.Workspaces.First(x => x.Model == CurrentSpace);
                vm.OnCurrentOffsetChanged(this, new PointEventArgs(new Point(cx, cy)));

                XmlNodeList elNodes = xmlDoc.GetElementsByTagName("dynElements");
                XmlNodeList cNodes = xmlDoc.GetElementsByTagName("dynConnectors");
                XmlNodeList nNodes = xmlDoc.GetElementsByTagName("dynNotes");

                XmlNode elNodesList = elNodes[0];
                XmlNode cNodesList = cNodes[0];
                XmlNode nNodesList = nNodes[0];

                //if there is any problem loading a node, then
                //add the node's guid to the bad nodes collection
                //so we can avoid attempting to make connections to it
                List<Guid> badNodes = new List<Guid>();

                foreach (XmlNode elNode in elNodesList.ChildNodes)
                {
                    XmlAttribute typeAttrib = elNode.Attributes["type"];
                    XmlAttribute guidAttrib = elNode.Attributes["guid"];
                    XmlAttribute nicknameAttrib = elNode.Attributes["nickname"];
                    XmlAttribute xAttrib = elNode.Attributes["x"];
                    XmlAttribute yAttrib = elNode.Attributes["y"];
                    XmlAttribute isVisAttrib = elNode.Attributes["isVisible"];
                    XmlAttribute isUpstreamVisAttrib = elNode.Attributes["isUpstreamVisible"];
                    XmlAttribute lacingAttrib = elNode.Attributes["lacing"];

                    string typeName = typeAttrib.Value;

                    //test the GUID to confirm that it is non-zero
                    //if it is zero, then we have to fix it
                    //this will break the connectors, but it won't keep
                    //propagating bad GUIDs
                    var guid = new Guid(guidAttrib.Value);
                    if (guid == Guid.Empty)
                    {
                        guid = Guid.NewGuid();
                    }

                    string nickname = nicknameAttrib.Value;

                    double x = double.Parse(xAttrib.Value, CultureInfo.InvariantCulture);
                    double y = double.Parse(yAttrib.Value, CultureInfo.InvariantCulture);

                    if (typeName.StartsWith("Dynamo.Elements."))
                        typeName = "Dynamo.Nodes." + typeName.Remove(0, 16);

                    TypeLoadData tData;
                    Type t;

                    if (!dynSettings.Controller.BuiltInTypesByName.TryGetValue(typeName, out tData))
                    {
                        //try and get a system type by this name
                        t = Type.GetType(typeName);

                        //if we still can't find the type, try the also known as attributes
                        if (t == null)
                        {
                            //try to get the also known as values
                            foreach (KeyValuePair<string, TypeLoadData> kvp in dynSettings.Controller.BuiltInTypesByName)
                            {
                                var akaAttribs = kvp.Value.Type.GetCustomAttributes(typeof(AlsoKnownAsAttribute), false);
                                if (akaAttribs.Count() > 0)
                                {
                                    if ((akaAttribs[0] as AlsoKnownAsAttribute).Values.Contains(typeName))
                                    {
                                        DynamoLogger.Instance.Log(string.Format("Found matching node for {0} also known as {1}", kvp.Key, typeName));
                                        t = kvp.Value.Type;
                                    }
                                }
                            }
                        }

                        if (t == null)
                        {
                            DynamoLogger.Instance.Log("Could not load node of type: " + typeName);
                            DynamoLogger.Instance.Log("Loading will continue but nodes might be missing from your workflow.");

                            //return false;
                            badNodes.Add(guid);
                            continue;
                        }
                    }
                    else
                        t = tData.Type;

                    bool isVisible = true;
                    if (isVisAttrib != null)
                        isVisible = isVisAttrib.Value == "true" ? true : false;

                    bool isUpstreamVisible = true;
                    if (isUpstreamVisAttrib != null)
                        isUpstreamVisible = isUpstreamVisAttrib.Value == "true" ? true : false;

                    dynNodeModel el = CreateNodeInstance(t, nickname, guid);
                    el.WorkSpace = CurrentSpace;
                    el.Load(elNode);

                    CurrentSpace.Nodes.Add(el);

                    el.X = x;
                    el.Y = y;

                    el.IsVisible = isVisible;
                    el.IsUpstreamVisible = isUpstreamVisible;

                    if (lacingAttrib != null)
                    {
                        if (el.ArgumentLacing != LacingStrategy.Disabled)
                        {
                            LacingStrategy lacing = LacingStrategy.Disabled;
                            Enum.TryParse(lacingAttrib.Value, out lacing);
                            el.ArgumentLacing = lacing;
                        }
                    }

                    el.DisableReporting();

                    if (CurrentSpace == HomeSpace)
                        el.SaveResult = true;
                }

                DynamoLogger.Instance.Log(string.Format("{0} ellapsed for loading nodes.", sw.Elapsed - previousElapsed));
                previousElapsed = sw.Elapsed;

                OnRequestLayoutUpdate(this, EventArgs.Empty);

                DynamoLogger.Instance.Log(string.Format("{0} ellapsed for updating layout.", sw.Elapsed - previousElapsed));
                previousElapsed = sw.Elapsed;

                foreach (XmlNode connector in cNodesList.ChildNodes)
                {
                    XmlAttribute guidStartAttrib = connector.Attributes[0];
                    XmlAttribute intStartAttrib = connector.Attributes[1];
                    XmlAttribute guidEndAttrib = connector.Attributes[2];
                    XmlAttribute intEndAttrib = connector.Attributes[3];
                    XmlAttribute portTypeAttrib = connector.Attributes[4];

                    var guidStart = new Guid(guidStartAttrib.Value);
                    var guidEnd = new Guid(guidEndAttrib.Value);
                    int startIndex = Convert.ToInt16(intStartAttrib.Value);
                    int endIndex = Convert.ToInt16(intEndAttrib.Value);
                    int portType = Convert.ToInt16(portTypeAttrib.Value);

                    //find the elements to connect
                    dynNodeModel start = null;
                    dynNodeModel end = null;

                    if (badNodes.Contains(guidStart) || badNodes.Contains(guidEnd))
                        continue;

                    foreach (dynNodeModel e in Nodes)
                    {
                        if (e.GUID == guidStart)
                        {
                            start = e;
                        }
                        else if (e.GUID == guidEnd)
                        {
                            end = e;
                        }
                        if (start != null && end != null)
                        {
                            break;
                        }
                    }

                    var newConnector = dynConnectorModel.Make(start, end,
                                                        startIndex, endIndex, portType);

                    Stopwatch addTimer = new Stopwatch();
                    addTimer.Start();
                    if (newConnector != null)
                        CurrentSpace.Connectors.Add(newConnector);
                    addTimer.Stop();
                    Debug.WriteLine(string.Format("{0} elapsed for add connector to collection.", addTimer.Elapsed));

                }

                DynamoLogger.Instance.Log(string.Format("{0} ellapsed for loading connectors.", sw.Elapsed - previousElapsed));
                previousElapsed = sw.Elapsed;

                #region instantiate notes

                if (nNodesList != null)
                {
                    foreach (XmlNode note in nNodesList.ChildNodes)
                    {
                        XmlAttribute textAttrib = note.Attributes[0];
                        XmlAttribute xAttrib = note.Attributes[1];
                        XmlAttribute yAttrib = note.Attributes[2];

                        string text = textAttrib.Value;
                        double x = double.Parse(xAttrib.Value, CultureInfo.InvariantCulture);
                        double y = double.Parse(yAttrib.Value, CultureInfo.InvariantCulture);

                        var paramDict = new Dictionary<string, object>();
                        paramDict.Add("x", x);
                        paramDict.Add("y", y);
                        paramDict.Add("text", text);
                        paramDict.Add("workspace", CurrentSpace);
                        
                        AddNote(paramDict);
                    }
                }

                #endregion

                DynamoLogger.Instance.Log(string.Format("{0} ellapsed for loading notes.", sw.Elapsed - previousElapsed));

                foreach (dynNodeModel e in CurrentSpace.Nodes)
                    e.EnableReporting();

                #endregion

                HomeSpace.FilePath = xmlPath;

                DynamoLogger.Instance.Log(string.Format("{0} ellapsed for loading workspace.", sw.Elapsed));
            }
            catch (Exception ex)
            {
                DynamoLogger.Instance.Log("There was an error opening the workbench.");
                DynamoLogger.Instance.Log(ex);
                Debug.WriteLine(ex.Message + ":" + ex.StackTrace);
                CleanWorkbench();
                return false;
            }
            return true;
        }

        internal FunctionDefinition NewFunction(Guid id,
                                        string name,
                                        string category,
                                        string description,
                                        bool display,
                                        double workspaceOffsetX = 0,
                                        double workspaceOffsetY = 0)
        {
            //Add an entry to the funcdict
            var workSpace = new FuncWorkspace(
                name, category, description, workspaceOffsetX, workspaceOffsetY)
            {
                WatchChanges = true
            };

            Workspaces.Add(workSpace);

            List<dynNodeModel> newElements = workSpace.Nodes.ToList();
            List<dynConnectorModel> newConnectors = workSpace.Connectors.ToList();

            var functionDefinition = new FunctionDefinition(id)
            {
                Workspace = workSpace
            };

            dynSettings.Controller.CustomNodeManager.AddFunctionDefinition(functionDefinition.FunctionId, functionDefinition);

            // add the element to search
            dynSettings.Controller.SearchViewModel.Add(name, category, description, id);

            if (display)
            {
                if (CurrentSpace != HomeSpace)
                {
                    var def = dynSettings.Controller.CustomNodeManager.GetDefinitionFromWorkspace(CurrentSpace);
                    if (def != null)
                        SaveFunction(def);
                }

                CurrentSpace = workSpace;
            }

            return functionDefinition;
        }

        /// <summary>
        ///     Save a function.  This includes writing to a file and compiling the 
        ///     function and saving it to the FSchemeEnvironment
        /// </summary>
        /// <param name="definition">The definition to saveo</param>
        /// <param name="bool">Whether to write the function to file.</param>
        /// <returns>Whether the operation was successful</returns>
        public void SaveFunction(FunctionDefinition definition, bool writeDefinition = true, bool addToSearch = false, bool compileFunction = true)
        {
            if (definition == null)
                return;

            // Get the internal nodes for the function
            var functionWorkspace = definition.Workspace as FuncWorkspace;

            // If asked to, write the definition to file
            if (writeDefinition)
            {
                string path = "";
                if (String.IsNullOrEmpty(definition.Workspace.FilePath))
                {
                    var pluginsPath = dynSettings.Controller.CustomNodeManager.GetDefaultSearchPath();

                    if (!Directory.Exists(pluginsPath))
                        Directory.CreateDirectory(pluginsPath);

                    path = Path.Combine(pluginsPath, dynSettings.FormatFileName(functionWorkspace.Name) + ".dyf");
                }
                else
                {
                    path = definition.Workspace.FilePath;
                }

                try
                {

                    if (addToSearch)
                    {
                        dynSettings.Controller.SearchViewModel.Add(functionWorkspace.Name, functionWorkspace.Category,functionWorkspace.Description, definition.FunctionId);
                    }

                    var info = new CustomNodeInfo(definition.FunctionId, functionWorkspace.Name, functionWorkspace.Category, functionWorkspace.Description, path);
                    dynSettings.Controller.CustomNodeManager.SetNodeInfo(info);

                    dynWorkspaceModel.SaveWorkspace(path, functionWorkspace);

                    #region Compile Function and update all nodes

                    IEnumerable<string> inputNames = new List<string>();
                    IEnumerable<string> outputNames = new List<string>();
                    dynSettings.Controller.FSchemeEnvironment.DefineSymbol(definition.FunctionId.ToString(), CustomNodeManager.CompileFunction(definition, ref inputNames, ref outputNames));

                    //Update existing function nodes which point to this function to match its changes
                    foreach (dynNodeModel el in AllNodes)
                    {
                        if (el is dynFunction)
                        {
                            var node = (dynFunction)el;

                            if (node.Definition != definition)
                                continue;

                            node.SetInputs(inputNames);
                            node.SetOutputs(outputNames);
                            el.RegisterAllPorts();
                        }
                    }

                    //Call OnSave for all saved elements
                    foreach (dynNodeModel el in functionWorkspace.Nodes)
                        el.onSave();


                    #endregion

                }
                catch (Exception e)
                {
                    DynamoLogger.Instance.Log("Error saving:" + e.GetType());
                    DynamoLogger.Instance.Log(e);
                }
            }

        }

        /// <summary>
        /// Write a message to the log.
        /// </summary>
        /// <param name="parameters">The message.</param>
        public void WriteToLog(object parameters)
        {
            if (parameters == null) return;
            string logText = parameters.ToString();
            DynamoLogger.Instance.Log(logText);
        }

        internal bool CanWriteToLog(object parameters)
        {
            if (DynamoLogger.Instance != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Add a note to the workspace.
        /// </summary>
        /// <param name="parameters">A dictionary containing placement data for the note</param>
        /// <example>{"x":1234.0,"y":1234.0, "guid":1234-1234-...,"text":"the note's text","workspace":workspace </example>
        public void AddNote(object parameters)
        {

            var inputs = parameters as Dictionary<string, object> ?? new Dictionary<string, object>();

            // by default place note at center
            var x = 0.0;
            var y = 0.0;

            if (inputs != null && inputs.ContainsKey("x"))
                x = (double)inputs["x"];

            if (inputs != null && inputs.ContainsKey("y"))

                y = (double)inputs["y"];

            var n = new dynNoteModel(x, y);

            //if we have null parameters, the note is being added
            //from the menu, center the view on the note

            if (parameters == null)
            {
                inputs.Add("transformFromOuterCanvasCoordinates", true);
                dynSettings.Controller.DynamoViewModel.CurrentSpaceViewModel.OnRequestNodeCentered(this, new ModelEventArgs(n, inputs));
            }

            object id;
            if (inputs.TryGetValue("guid", out id))
                n.GUID = (Guid)id;

            n.Text = (inputs == null || !inputs.ContainsKey("text")) ? "New Note" : inputs["text"].ToString();
            var ws = (inputs == null || !inputs.ContainsKey("workspace")) ? CurrentSpace : (dynWorkspaceModel)inputs["workspace"];

            ws.Notes.Add(n);

        }

        internal bool CanAddNote(object parameters)
        {
            return true;
        }

        /// <summary>
        /// Copy selected ISelectable objects to the clipboard.
        /// </summary>
        /// <param name="parameters"></param>
        public void Copy(object parameters)
        {
            dynSettings.Controller.ClipBoard.Clear();

            foreach (ISelectable sel in DynamoSelection.Instance.Selection)
            {
                //MVVM : selection and clipboard now hold view model objects
                //UIElement el = sel as UIElement;
                dynModelBase el = sel as dynModelBase;
                if (el != null)
                {
                    if (!dynSettings.Controller.ClipBoard.Contains(el))
                    {
                        dynSettings.Controller.ClipBoard.Add(el);

                        //dynNodeView n = el as dynNodeView;
                        dynNodeModel n = el as dynNodeModel;
                        if (n != null)
                        {
                            var connectors = n.InPorts.ToList().SelectMany(x => x.Connectors)
                                .Concat(n.OutPorts.ToList().SelectMany(x => x.Connectors))
                                .Where(x => x.End != null &&
                                    x.End.Owner.IsSelected &&
                                    !dynSettings.Controller.ClipBoard.Contains(x));

                            dynSettings.Controller.ClipBoard.AddRange(connectors);
                        }
                    }
                }
            }
        }

        internal bool CanCopy(object parameters)
        {
            if (DynamoSelection.Instance.Selection.Count == 0)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Paste ISelectable objects from the clipboard to the workspace.
        /// </summary>
        /// <param name="parameters"></param>
        public void Paste(object parameters)
        {
            //make a lookup table to store the guids of the
            //old nodes and the guids of their pasted versions
            var nodeLookup = new Dictionary<Guid, Guid>();

            //clear the selection so we can put the
            //paste contents in
            DynamoSelection.Instance.Selection.RemoveAll();

            var nodes = dynSettings.Controller.ClipBoard.OfType<dynNodeModel>();

            var connectors = dynSettings.Controller.ClipBoard.OfType<dynConnectorModel>();

            foreach (dynNodeModel node in nodes)
            {
                //create a new guid for us to use
                Guid newGuid = Guid.NewGuid();
                nodeLookup.Add(node.GUID, newGuid);

                var nodeData = new Dictionary<string, object>();
                nodeData.Add("x", node.X);
                nodeData.Add("y", node.Y + 100);
                if (node is dynFunction)
                    nodeData.Add("name", (node as dynFunction).Definition.FunctionId);
                else
                    nodeData.Add("name", node.GetType());
                nodeData.Add("guid", newGuid);

                var xmlDoc = new XmlDocument();
                var dynEl = xmlDoc.CreateElement(node.GetType().ToString());
                xmlDoc.AppendChild(dynEl);
                node.Save(xmlDoc, dynEl, SaveContext.Copy);

                nodeData.Add("data", dynEl);

                //dynSettings.Controller.CommandQueue.Enqueue(Tuple.Create<object, object>(CreateNodeCommand, nodeData));
                CreateNode(nodeData);
            }

            //process the command queue so we have 
            //nodes to connect to
            //DynamoCommands.ProcessCommandQueue();

            //update the layout to ensure that the visuals
            //are present in the tree to connect to
            //dynSettings.Bench.UpdateLayout();
            OnRequestLayoutUpdate(this, EventArgs.Empty);

            foreach (dynConnectorModel c in connectors)
            {
                var connectionData = new Dictionary<string, object>();

                // if in nodeLookup, the node is paste.  otherwise, use the existing node guid
                Guid startGuid = Guid.Empty;
                Guid endGuid = Guid.Empty;

                startGuid = nodeLookup.TryGetValue(c.Start.Owner.GUID, out startGuid) ? startGuid : c.Start.Owner.GUID;
                endGuid = nodeLookup.TryGetValue(c.End.Owner.GUID, out endGuid) ? endGuid : c.End.Owner.GUID;

                var startNode = CurrentSpace.Nodes.FirstOrDefault(x => x.GUID == startGuid);
                var endNode = CurrentSpace.Nodes.FirstOrDefault(x => x.GUID == endGuid);

                // do not form connector if the end nodes are null
                if (startNode == null || endNode == null)
                {
                    continue;
                }

                //don't let users paste connectors between workspaces
                if (startNode.WorkSpace != CurrentSpace)
                {
                    continue;
                }

                connectionData.Add("start", startNode);
                connectionData.Add("end", endNode);

                connectionData.Add("port_start", c.Start.Index);
                connectionData.Add("port_end", c.End.Index);

                //dynSettings.Controller.CommandQueue.Enqueue(Tuple.Create<object, object>(CreateConnectionCommand, connectionData));
                CreateConnection(connectionData);
            }

            //process the queue again to create the connectors
            //DynamoCommands.ProcessCommandQueue();

            var notes = dynSettings.Controller.ClipBoard.OfType<dynNoteModel>();

            foreach (dynNoteModel note in notes)
            {
                var newGUID = Guid.NewGuid();

                var sameSpace = CurrentSpace.Notes.Any(x => x.GUID == note.GUID);
                var newX = sameSpace ? note.X + 20 : note.X;
                var newY = sameSpace ? note.Y + 20 : note.Y;

                var noteData = new Dictionary<string, object>()
                {
                    { "x", newX },
                    { "y", newY },
                    { "text", note.Text },
                    { "guid", newGUID }
                };

                AddNote(noteData);

                AddToSelection(CurrentSpace.Notes.FirstOrDefault(x => x.GUID == newGUID));
            }

            foreach (var de in nodeLookup)
            {
                AddToSelection(CurrentSpace.Nodes.FirstOrDefault(x => x.GUID == de.Value));
            }
        }

        internal bool CanPaste(object parameters)
        {
            if (dynSettings.Controller.ClipBoard.Count == 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Add an ISelectable object to the selection.
        /// </summary>
        /// <param name="parameters">The object to add to the selection.</param>
        public void AddToSelection(object parameters)
        {
            var node = parameters as dynNodeModel;
            
            //don't add if the object is null
            if (node == null)
                return;

            if (!node.IsSelected)
            {
                if (!DynamoSelection.Instance.Selection.Contains(node))
                    DynamoSelection.Instance.Selection.Add(node);
            }
        }

        internal bool CanAddToSelection(object parameters)
        {
            var node = parameters as dynNodeModel;
            if (node == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Present the new function dialogue and create a custom function.
        /// </summary>
        /// <param name="parameter"></param>
        public void ShowNewFunctionDialogAndMakeFunction(object parameter)
        {
            //trigger the event to request the display
            //of the function name dialogue
            var args = new FunctionNamePromptEventArgs();
            OnRequestsFunctionNamePrompt(this, args);

            //string name = "", category = "";
            //if (ShowNewFunctionDialog(ref name, ref category))
            if (args.Success)
            {
                //NewFunction(Guid.NewGuid(), name, category, true);
                NewFunction(Guid.NewGuid(), args.Name, args.Category, args.Description, true);
            }
        }

        internal bool CanShowNewFunctionDialogCommand(object parameter)
        {
            return true;
        }

        /// <summary>
        /// Create a node.
        /// </summary>
        /// <param name="parameters">A dictionary containing data about the node.</param>
        public void CreateNode(object parameters)
        {
            var data = parameters as Dictionary<string, object>;
            if (data == null)
            {
                return;
            }

            dynNodeModel node = CreateNode(data["name"].ToString());
            if (node == null)
            {
                dynSettings.Controller.DynamoModel.WriteToLog("Failed to create the node");
                return;
            }

            if ((node is dynSymbol || node is dynOutput) && CurrentSpace is HomeWorkspace)
            {
                dynSettings.Controller.DynamoModel.WriteToLog("Cannot place dynSymbol or dynOutput in HomeWorkspace");
                return;
            }

            CurrentSpace.Nodes.Add(node);
            node.WorkSpace = CurrentSpace;

            //if we've received a value in the dictionary
            //try to set the value on the node
            if (data.ContainsKey("data"))
            {
                node.Load(data["data"] as XmlNode);
            }

            //override the guid so we can store
            //for connection lookup
            if (data.ContainsKey("guid"))
            {
                node.GUID = (Guid)data["guid"];
            }
            else
            {
                node.GUID = Guid.NewGuid();
            }

            dynSettings.Controller.DynamoViewModel.CurrentSpaceViewModel.OnRequestNodeCentered(this, new ModelEventArgs(node, data));

            node.EnableInteraction();

            if (CurrentSpace == HomeSpace)
            {
                node.SaveResult = true;
            }
        }

        internal bool CanCreateNode(object parameters)
        {
            var data = parameters as Dictionary<string, object>;

            if (data == null)
                return false;

            Guid guid;
            var name = data["name"].ToString();

            if (dynSettings.Controller.BuiltInTypesByNickname.ContainsKey(name)
                    || dynSettings.Controller.BuiltInTypesByName.ContainsKey(name)
                    || (Guid.TryParse(name, out guid) && dynSettings.Controller.CustomNodeManager.Contains(guid)))
            {
                return true;
            }

            string message = string.Format("Can not create instance of node {0}.", data["name"]);
            dynSettings.Controller.DynamoModel.WriteToLog(message);
            DynamoLogger.Instance.Log(message);

            return false;
        }

        internal dynNodeModel CreateNode(string name)
        {
            dynNodeModel result;

            if (dynSettings.Controller.BuiltInTypesByName.ContainsKey(name))
            {
                TypeLoadData tld = dynSettings.Controller.BuiltInTypesByName[name];

                ObjectHandle obj = Activator.CreateInstanceFrom(tld.Assembly.Location, tld.Type.FullName);
                var newEl = (dynNodeModel)obj.Unwrap();
                newEl.DisableInteraction();
                result = newEl;
            }
            else if (dynSettings.Controller.BuiltInTypesByNickname.ContainsKey(name))
            {
                TypeLoadData tld = dynSettings.Controller.BuiltInTypesByNickname[name];
                try
                {

                    ObjectHandle obj = Activator.CreateInstanceFrom(tld.Assembly.Location, tld.Type.FullName);
                    var newEl = (dynNodeModel)obj.Unwrap();
                    newEl.DisableInteraction();
                    result = newEl;
                }
                catch (Exception ex)
                {
                    DynamoLogger.Instance.Log("Failed to load built-in type");
                    DynamoLogger.Instance.Log(ex);
                    result = null;
                }
            }
            else
            {
                dynFunction func;

                if (dynSettings.Controller.CustomNodeManager.GetNodeInstance(Guid.Parse(name), out func))
                {
                    result = func;
                }
                else
                {
                    DynamoLogger.Instance.Log("Failed to find FunctionDefinition.");
                    return null;
                }
            }

            return result;
        }

        /// <summary>
        /// Create a connector.
        /// </summary>
        /// <param name="parameters">A dictionary containing data about the connection.</param>
        public void CreateConnection(object parameters)
        {
            try
            {
                Dictionary<string, object> connectionData = parameters as Dictionary<string, object>;

                dynNodeModel start = (dynNodeModel)connectionData["start"];
                dynNodeModel end = (dynNodeModel)connectionData["end"];
                int startIndex = (int)connectionData["port_start"];
                int endIndex = (int)connectionData["port_end"];

                var c = dynConnectorModel.Make(start, end, startIndex, endIndex, 0);

                if (c != null)
                    CurrentSpace.Connectors.Add(c);
            }
            catch (Exception e)
            {
                DynamoLogger.Instance.Log(e.Message);
                DynamoLogger.Instance.Log(e);
            }
        }

        internal bool CanCreateConnection(object parameters)
        {
            //make sure you have valid connection data
            Dictionary<string, object> connectionData = parameters as Dictionary<string, object>;
            if (connectionData != null && connectionData.Count == 4)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Save the current workspace to a specific file path, if the path is null or empty, does nothing.
        ///     If successful, the CurrentSpace.FilePath field is updated as a side effect
        /// </summary>
        /// <param name="path">The path to save to</param>
        internal void SaveAs(string path)
        {
            this.SaveAs(path, CurrentSpace);
        }

        /// <summary>
        ///     Save to a specific file path, if the path is null or empty, does nothing.
        ///     If successful, the CurrentSpace.FilePath field is updated as a side effect
        /// </summary>
        /// <param name="path">The path to save to</param>
        /// <param name="workspace">The workspace to save</param>
        internal void SaveAs(string path, dynWorkspaceModel workspace)
        {
            if (!String.IsNullOrEmpty(path))
            {
                // if it's a custom node
                if (workspace is FuncWorkspace)
                {
                    var def = dynSettings.Controller.CustomNodeManager.GetDefinitionFromWorkspace(workspace);
                    def.Workspace.FilePath = path;

                    if (def != null)
                    {
                        this.SaveFunction(def, true);
                        workspace.FilePath = path;
                    }
                    return;
                }

                if (!dynWorkspaceModel.SaveWorkspace(path, workspace))
                {
                    DynamoLogger.Instance.Log("Workbench could not be saved.");
                }
                else
                {
                    workspace.FilePath = path;
                }

            }
        }

        /// <summary>
        ///     Attempts to save an element, assuming that the CurrentSpace.FilePath 
        ///     field is already  populated with a path has a filename associated with it. 
        /// </summary>
        public void Save(object parameter)
        {
            if (!String.IsNullOrEmpty(CurrentSpace.FilePath))
                SaveAs(CurrentSpace.FilePath);
        }

        internal bool CanSave(object parameter)
        {
            return true;
        }

        /// <summary>
        /// Save the current workspace.
        /// </summary>
        /// <param name="parameters">The file path.</param>
        public void SaveAs(object parameters)
        {
            if (parameters == null)
                return;

            var fi = new FileInfo(parameters.ToString());
            
            SaveAs(fi.FullName);
        }

        internal bool CanSaveAs(object parameters)
        {
            if (parameters == null)
                return false;

            return true;
        }

        /// <summary>
        /// Delete ISelectable objects.
        /// </summary>
        /// <param name="parameters">The objects to delete.</param>
        public void Delete(object parameters)
        {
            //if you get an object in the parameters, just delete that object
            if (parameters != null)
            {
                var note = parameters as dynNoteModel;
                var node = parameters as dynNodeModel;

                if (node != null)
                {
                    DeleteNodeAndItsConnectors(node);
                }
                else if (note != null)
                {
                    DeleteNote(note);
                }
            }
            else
            {
                for (int i = DynamoSelection.Instance.Selection.Count - 1; i >= 0; i--)
                {
                    var note = DynamoSelection.Instance.Selection[i] as dynNoteModel;
                    var node = DynamoSelection.Instance.Selection[i] as dynNodeModel;

                    if (node != null)
                    {
                        DeleteNodeAndItsConnectors(node);
                    }
                    else if (note != null)
                    {
                        DeleteNote(note);
                    }
                }
            }
        }

        internal bool CanDelete(object parameters)
        {
            return DynamoSelection.Instance.Selection.Count > 0;
        }

        /// <summary>
        /// Delete a note.
        /// </summary>
        /// <param name="note">The note to delete.</param>
        public void DeleteNote(dynNoteModel note)
        {
            DynamoSelection.Instance.Selection.Remove(note);
            CurrentSpace.Notes.Remove(note);
        }

        private static void DeleteNodeAndItsConnectors(dynNodeModel node)
        {
            foreach (var conn in node.AllConnectors().ToList())
            {
                conn.NotifyConnectedPortsOfDeletion();
                dynSettings.Controller.DynamoViewModel.Model.CurrentSpace.Connectors.Remove(conn);
            }

            node.DisableReporting();
            node.Destroy();
            node.Cleanup();
            DynamoSelection.Instance.Selection.Remove(node);
            node.WorkSpace.Nodes.Remove(node);
        }

        /// <summary>
        ///     Update a custom node after refactoring.  Updates search and all instances of the node.
        /// </summary>
        /// <param name="selectedNodes"> The function definition for the user-defined node </param>
        public void RefactorCustomNode(object parameter)
        {
            //Bench.workspaceLabel.Content = Bench.editNameBox.Text;
            var def = dynSettings.Controller.CustomNodeManager.GetDefinitionFromWorkspace(CurrentSpace);

            //TODO: UI Refactor - Is this the right data for refactor?
            var info = new CustomNodeInfo(def.FunctionId, editName, CurrentSpace.Category, CurrentSpace.Description, CurrentSpace.FilePath);

            dynSettings.Controller.SearchViewModel.Refactor(info);

            //Update existing function nodes
            foreach (dynNodeModel el in AllNodes)
            {
                if (el is dynFunction)
                {
                    var node = (dynFunction)el;

                    if (node.Definition == null)
                    {
                        node.Definition = dynSettings.Controller.CustomNodeManager.GetFunctionDefinition(Guid.Parse(node.Symbol));
                    }

                    if (!node.Definition.Workspace.Name.Equals(CurrentSpace.Name))
                        continue;

                    //Rename nickname only if it's still referring to the old name
                    if (node.NickName.Equals(CurrentSpace.Name))
                        node.NickName = editName;
                }
            }

            dynSettings.Controller.FSchemeEnvironment.RemoveSymbol(CurrentSpace.Name);

            //TODO: Delete old stored definition
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string pluginsPath = Path.Combine(directory, "definitions");

            if (Directory.Exists(pluginsPath))
            {
                string oldpath = Path.Combine(pluginsPath, CurrentSpace.Name + ".dyf");
                if (File.Exists(oldpath))
                {
                    string newpath = dynSettings.FormatFileName(
                        Path.Combine(pluginsPath, editName + ".dyf")
                        );

                    File.Move(oldpath, newpath);
                }
            }

            (CurrentSpace).Name = editName;

            SaveFunction(def);
        }

        internal bool CanRefactorCustomNode(object parameter)
        {
            return true;
        }

        /// <summary>
        /// Clear the workspace. Removes all nodes, notes, and connectors from the current workspace.
        /// </summary>
        /// <param name="parameter"></param>
        public void Clear(object parameter)
        {
            dynSettings.Controller.IsUILocked = true;

            CleanWorkbench();

            //don't save the file path
            CurrentSpace.FilePath = "";
            CurrentSpace.HasUnsavedChanges = false;

            //clear the renderables
            dynSettings.Controller.RenderDescriptions.Clear();
            dynSettings.Controller.OnRequestsRedraw(dynSettings.Controller, EventArgs.Empty);

            dynSettings.Controller.IsUILocked = false;
        }

        internal bool CanClear(object parameter)
        {
            return true;
        }

        /// <summary>
        /// View the home workspace.
        /// </summary>
        /// <param name="parameter"></param>
        public void Home(object parameter)
        {
            ViewHomeWorkspace();
        }

        internal bool CanGoHome(object parameter)
        {
            return CurrentSpace != HomeSpace;
        }

        /// <summary>
        /// Layout all available nodes in columns by category.
        /// </summary>
        /// <param name="parameter"></param>
        public void LayoutAll(object parameter)
        {
            dynSettings.Controller.IsUILocked = true;

            CleanWorkbench();

            double x = 0;
            double y = 0;
            double maxWidth = 0;    //track max width of current column
            double colGutter = 40;     //the space between columns
            double rowGutter = 40;
            int colCount = 0;

            Hashtable typeHash = new Hashtable();

            foreach (KeyValuePair<string, TypeLoadData> kvp in dynSettings.Controller.BuiltInTypesByNickname)
            {
                Type t = kvp.Value.Type;

                object[] attribs = t.GetCustomAttributes(typeof(NodeCategoryAttribute), false);

                if (t.Namespace == "Dynamo.Nodes" &&
                    !t.IsAbstract &&
                    attribs.Length > 0 &&
                    t.IsSubclassOf(typeof(dynNodeModel)))
                {
                    NodeCategoryAttribute elCatAttrib = attribs[0] as NodeCategoryAttribute;

                    List<Type> catTypes = null;

                    if (typeHash.ContainsKey(elCatAttrib.ElementCategory))
                    {
                        catTypes = typeHash[elCatAttrib.ElementCategory] as List<Type>;
                    }
                    else
                    {
                        catTypes = new List<Type>();
                        typeHash.Add(elCatAttrib.ElementCategory, catTypes);
                    }

                    catTypes.Add(t);
                }
            }

            foreach (DictionaryEntry de in typeHash)
            {
                List<Type> catTypes = de.Value as List<Type>;

                //add the name of the category here
                //AddNote(de.Key.ToString(), x, y, ViewModel.CurrentSpace);
                Dictionary<string, object> paramDict = new Dictionary<string, object>();
                paramDict.Add("x", x);
                paramDict.Add("y", y);
                paramDict.Add("text", de.Key.ToString());
                paramDict.Add("workspace", CurrentSpace);

                if (CanAddNote(paramDict))
                    AddNote(paramDict);

                y += 60;

                foreach (Type t in catTypes)
                {
                    object[] attribs = t.GetCustomAttributes(typeof(NodeNameAttribute), false);

                    NodeNameAttribute elNameAttrib = attribs[0] as NodeNameAttribute;
                    dynNodeModel el = CreateInstanceAndAddNodeToWorkspace(
                           t, elNameAttrib.Name, Guid.NewGuid(), x, y,
                           CurrentSpace
                        );

                    if (el == null) continue;

                    el.DisableReporting();

                    maxWidth = Math.Max(el.Width, maxWidth);

                    colCount++;

                    y += el.Height + rowGutter;

                    if (colCount > 20)
                    {
                        y = 60;
                        colCount = 0;
                        x += maxWidth + colGutter;
                        maxWidth = 0;
                    }
                }

                y = 0;
                colCount = 0;
                x += maxWidth + colGutter;
                maxWidth = 0;

            }

            dynSettings.Controller.IsUILocked = false;
        }

        internal bool CanLayoutAll(object parameter)
        {
            return true;
        }
    }

    public class DynamoModelUpdateArgs : EventArgs
    {
        public object Item { get; set; }

        public DynamoModelUpdateArgs(object item)
        {
            Item = item;
        }
    }
}
