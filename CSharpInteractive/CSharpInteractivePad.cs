using System;
using Gdk;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Gui;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace CSharpInteractive
{
	enum KillIntent
	{
		None,
		Restart,
	}

	public class CSharpInteractivePad : IPadContent
	{
		readonly ConsoleView view;

		IInteractiveSession session;

		bool isPrompting = false;
		KillIntent kill = KillIntent.None;

		public CSharpInteractivePad ()
		{
			view = new ConsoleView ();
			session = SetupSession ();
		}

		#region Commands

		public void SendSelection ()
		{
			var doc = IdeApp.Workbench.ActiveDocument;
			var sel = doc.Editor.SelectedText;

			if (sel == null) {

				var ed = doc.Editor;
				var s = ed.Caret;

				var line = doc.Editor.GetLineText (s.Line).Trim ();
				SendCommand (line, line);
			}
			else {
				var t = sel.Trim ();
				SendCommand (t, t);
			}
		}

		public void SendType ()
		{
			var doc = IdeApp.Workbench.ActiveDocument;

			var ed = doc.Editor;
			var s = ed.Caret;

			var parsed = doc.ParsedDocument;
			if (parsed == null)
				return;

			var t = parsed.GetTopLevelTypeDefinition (new ICSharpCode.NRefactory.TextLocation (s.Line, s.Column));
			if (t == null)
				return;

			var tr = t.Region;
			var begin = tr.BeginLine;
			var end = tr.EndLine;
			var sb = new StringBuilder ();
			var ns = t.Namespace;
			if (!string.IsNullOrEmpty (ns)) {
				sb.AppendLine ("namespace " + ns + " {");
			}
			for (var i = begin; i > 0 && i <= end; ++i) {
				sb.AppendLine (ed.GetLineText (i));
			}
			if (!string.IsNullOrEmpty (ns)) {
				sb.AppendLine ("}");
			}

			var ts = sb.ToString ();

			var typeBegin = tr.Begin;
			var bodyBegin = t.BodyRegion.Begin;

			var name = ed.GetTextBetween (typeBegin, bodyBegin).Trim () + " { ... }";

			SendCommand (name, ts);
			SendUsing (ns);
			SendCommand ("", "typeof(" + t.FullName + ")");
		}

		#endregion

		IInteractiveSession SetupSession ()
		{
			var ses = new CSharpInteractiveSession ();
			ses.TextReceived += t => DispatchService.GuiDispatch (() => view.WriteOutput (t));
			ses.PromptReady += () => DispatchService.GuiDispatch (() => view.Prompt (true));
			ses.Exited += () => DispatchService.GuiDispatch (() => {
				if (kill == KillIntent.None) {
					view.WriteOutput ("\nSession termination detected. Press Enter to restart.");
					isPrompting = true;
				} else if (kill == KillIntent.Restart) {
					view.Clear ();
				}
				kill = KillIntent.None;
			});
			ses.StartReceiving ();
			return ses;
		}

		void RestartCsi ()
		{
		}

		void Shutdown ()
		{
		}

		void HandleConsoleInput (object sender, ConsoleInputEventArgs e)
		{
			if (isPrompting) {
				isPrompting = false;
				session = null;
				SendCommand ("", "");
			} else {
				SendCommand ("", e.Text);
			}
		}

		void HandleActiveDocumentChanged (object sender, EventArgs e)
		{

		}

		readonly HashSet<string> usings = new HashSet<string> ();

		void SendUsing (string ns)
		{
			if (string.IsNullOrWhiteSpace (ns))
				return;

			if (usings.Contains (ns))
				return;

			usings.Add (ns);
			SendCommand ("","using " + ns + ";\n");
		}

		void SendCommand (string echo, string line)
		{
			if (string.IsNullOrWhiteSpace (line))
				return;

			if (session == null) {
				session = SetupSession ();
			}

			var text = line.EndsWith ("\n", StringComparison.Ordinal) ? line : line + "\n";

			var tv = view.Child as Gtk.TextView;
			if (tv != null && !string.IsNullOrEmpty (echo)) {
				var iter = tv.Buffer.EndIter;
				tv.Buffer.Insert (ref iter, echo + "\n");
			}

			session.SendCommand (text);
		}

		#region Pad Content

		public void Initialize (IPadWindow window)
		{
			//
			// Handle normal input
			//
			view.ConsoleInput += HandleConsoleInput;

			//
			// Setup Ctrl + / as the interrupt handler
			//
			view.Child.KeyPressEvent += (o, args) => {
				if ((args.Event.State & ModifierType.ControlMask) == ModifierType.ControlMask &&
					args.Event.Key == Key.slash) {
					if (session != null) session.Interrupt ();
				}
			};

			//
			// Watch the active doc
			//
			IdeApp.Workbench.ActiveDocumentChanged += HandleActiveDocumentChanged;

			//
			//
			//
			UpdateFont ();
			UpdateColors ();
			view.ShadowType = Gtk.ShadowType.None;
			view.ShowAll ();

			//
			// Add a Restart item to the pop up menu
			//
			var v = view.Child as Gtk.TextView;
			if (v != null) {
				v.PopulatePopup += (o, args) => {
					var item = new Gtk.MenuItem(GettextCatalog.GetString("Reset"));
					item.Activated += (sender, e) => RestartCsi ();
					item.Show ();
					args.Menu.Add (item);
				};
			}

			//
			// Create the toolbar
			//

		}

		void UpdateFont ()
		{
			var fontName = DesktopService.DefaultMonospaceFont;
			var font = Pango.FontDescription.FromString(fontName);
			view.SetFont(font);
		}

		void UpdateColors ()
		{
		}

		public void RedrawContent ()
		{
		}

		public Gtk.Widget Control {
			get {
				return view;
			}
		}

		public void Dispose ()
		{
			IdeApp.Workbench.ActiveDocumentChanged -= HandleActiveDocumentChanged;
			Shutdown ();
		}

		#endregion

		#region Cheat Data Access for Commands

		public static Pad CurrentPad {
			get {
				try {
					return IdeApp.Workbench.GetPad<CSharpInteractivePad>();
				} catch (Exception) {
				}

				return IdeApp.Workbench.AddPad (
					new CSharpInteractivePad (),
					"CSharpInteractive.CSharpInteractivePad",
					"C# Interactive", "Center Bottom", new IconId ("md-cs-project"));
			}
		}

		public static CSharpInteractivePad CurrentCsi {
			get {
				return CurrentPad.Content as CSharpInteractivePad;
			}
		}

		#endregion
	}
	
	public class ShowCSharpInteractive : CommandHandler
	{
		protected override void Run()
		{
			var pad = CSharpInteractivePad.CurrentPad;
			pad.BringToFront(true);
		}
		protected override void Update(CommandInfo info)
		{
			info.Enabled = true;
			info.Visible = true;
		}
	}

	public class SendSelection : CommandHandler
	{
		protected override void Run()
		{
			CSharpInteractivePad.CurrentCsi.SendSelection ();
			CSharpInteractivePad.CurrentPad.BringToFront (false);
		}
		protected override void Update(CommandInfo info)
		{
			info.Enabled = true;
			info.Visible = true;
		}
	}

	public class SendType : CommandHandler
	{
		protected override void Run()
		{
			CSharpInteractivePad.CurrentCsi.SendType ();
			CSharpInteractivePad.CurrentPad.BringToFront (false);
		}
		protected override void Update(CommandInfo info)
		{
			info.Enabled = true;
			info.Visible = true;
		}
	}
}

