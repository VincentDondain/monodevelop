//
// Authors:
//   Christian Hergert	<chris@mosaix.net>
//   Ben Motmans  <ben.motmans@gmail.com>
//
// Copyright (C) 2005 Mosaix Communications, Inc.
// Copyright (c) 2007 Ben Motmans
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using Gtk;
using System;
using System.Threading;
using System.Collections.Generic;
using MonoDevelop.Database.Sql;
using MonoDevelop.Database.Components;
using MonoDevelop.Database.Designer;
using MonoDevelop.Core;
using MonoDevelop.Core.Gui;
using MonoDevelop.Ide.Gui.Pads;
using MonoDevelop.Components.Commands;
using MonoDevelop.Ide.Gui.Components;

namespace MonoDevelop.Database.ConnectionManager
{
	public class TablesNodeBuilder : TypeNodeBuilder
	{
		private EventHandler RefreshHandler;
		
		public TablesNodeBuilder ()
			: base ()
		{
			RefreshHandler = new EventHandler (OnRefreshEvent);
		}
		
		public override Type NodeDataType {
			get { return typeof (TablesNode); }
		}
		
		public override string ContextMenuAddinPath {
			get { return "/MonoDevelop/Database/ContextMenu/ConnectionManagerPad/TablesNode"; }
		}
		
		public override Type CommandHandlerType {
			get { return typeof (TablesNodeCommandHandler); }
		}
		
		public override string GetNodeName (ITreeNavigator thisNode, object dataObject)
		{
			return AddinCatalog.GetString ("Tables");
		}
		
		public override void BuildNode (ITreeBuilder treeBuilder, object dataObject, ref string label, ref Gdk.Pixbuf icon, ref Gdk.Pixbuf closedIcon)
		{
			label = AddinCatalog.GetString ("Tables");
			icon = Context.GetIcon ("md-db-tables");
			
			BaseNode node = (BaseNode) dataObject;
			node.RefreshEvent += (EventHandler)(DispatchService.GuiDispatch (RefreshHandler));
		}
		
		public override void BuildChildNodes (ITreeBuilder builder, object dataObject)
		{
			ThreadPool.QueueUserWorkItem (new WaitCallback (BuildChildNodesThreaded), dataObject);
		}
		
		private void BuildChildNodesThreaded (object state)
		{
			BaseNode node = state as BaseNode;
			ITreeBuilder builder = Context.GetTreeBuilder (state);
			bool showSystemObjects = (bool)builder.Options["ShowSystemObjects"];
			TableSchemaCollection tables = node.ConnectionContext.SchemaProvider.GetTables ();
			
			DispatchService.GuiDispatch (delegate {
				foreach (TableSchema table in tables) {
					if (table.IsSystemTable && !showSystemObjects)
						continue;

					builder.AddChild (new TableNode (node.ConnectionContext, table));
				}
				builder.Expanded = true;
			});
		}
		
		public override bool HasChildNodes (ITreeBuilder builder, object dataObject)
		{
			return true;
		}
		
		private void OnRefreshEvent (object sender, EventArgs args)
		{
			ITreeBuilder builder = Context.GetTreeBuilder ();
			
			builder.UpdateChildren ();			
			builder.ExpandToNode ();
		}
	}
	
	public class TablesNodeCommandHandler : NodeCommandHandler
	{
		public override DragOperation CanDragNode ()
		{
			return DragOperation.None;
		}
		
		[CommandHandler (ConnectionManagerCommands.Refresh)]
		protected void OnRefresh ()
		{
			BaseNode node = CurrentNode.DataItem as BaseNode;
			node.Refresh ();
		}
		
		[CommandHandler (ConnectionManagerCommands.CreateTable)]
		protected void OnCreateTable ()
		{
			BaseNode node = CurrentNode.DataItem as BaseNode;
			IDbFactory fac = node.ConnectionContext.DbFactory;
			IEditSchemaProvider schemaProvider = (IEditSchemaProvider)node.ConnectionContext.SchemaProvider;
			
			// Need to detect if it is a previous (saved) table with the same name.
			string name = AddinCatalog.GetString("NewTable");
			int lastIdx = 0;
			TableSchemaCollection tables = schemaProvider.GetTables ();
			for (int t = tables.Count-1; t > -1; t--) {
				if (tables[t].Name.ToLower () == name.ToLower ()) {
					name = string.Concat (name, "1");
					break;
				} else if (tables[t].Name.StartsWith (name, StringComparison.OrdinalIgnoreCase)) {
					string idx = tables[t].Name.Substring (name.Length);
					int newIdx;
					if (int.TryParse (idx, out newIdx)) {
						lastIdx = newIdx;
						break;
					}
						
				}
			}
			if (lastIdx != 0)
				name = String.Concat (name, lastIdx+1);
			
			TableSchema table = schemaProvider.CreateTableSchema (name);
			if (fac.GuiProvider.ShowTableEditorDialog (schemaProvider, table, true))
				ThreadPool.QueueUserWorkItem (new WaitCallback (OnCreateTableThreaded), new object[] {schemaProvider, table, node} as object);
		}
		
		private void OnCreateTableThreaded (object state)
		{
			object[] objs = state as object[];
			
			ISchemaProvider provider = objs[0] as ISchemaProvider;
			TableSchema table = objs[1] as TableSchema;
			BaseNode node = objs[2] as BaseNode;

			LoggingService.LogDebug ("ADD TABLE: {0}", table.Definition);
			
			IPooledDbConnection conn = provider.ConnectionPool.Request ();
			conn.ExecuteNonQuery (table.Definition);
			conn.Release ();
			
			node.Refresh ();
		}
		
		[CommandUpdateHandler (ConnectionManagerCommands.CreateTable)]
		protected void OnUpdateCreateTable (CommandInfo info)
		{
			BaseNode node = (BaseNode)CurrentNode.DataItem;
			info.Enabled = node.ConnectionContext.SchemaProvider.IsSchemaActionSupported (SchemaType.Table, SchemaActions.Create);
		}
	}
}
